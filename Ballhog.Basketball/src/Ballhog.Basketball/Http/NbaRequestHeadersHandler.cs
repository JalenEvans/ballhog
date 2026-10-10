namespace Ballhog.Basketball.Http;

/// <summary>
/// A <see cref="DelegatingHandler"/> that applies Ballhog's 2026 NBA request headers
/// to every outgoing request.
/// </summary>
/// <remarks>
/// <para>
/// Every request receives the base header set: a Chrome-like <c>User-Agent</c>,
/// <c>Referer: https://www.nba.com/</c>, and an <c>Accept-Encoding</c> value.
/// </para>
/// <para>
/// Requests classified as CDN requests (see <see cref="IsCdnRequest"/>) additionally
/// receive the client-hint headers <c>Sec-Ch-Ua</c>, <c>Sec-Ch-Ua-Platform</c>,
/// <c>Sec-Fetch-Mode</c>, and <c>Sec-Fetch-Site</c>.
/// </para>
/// <para>
/// The <c>Host</c> header is never set directly; .NET forbids it and derives it from
/// the request URI instead.
/// </para>
/// </remarks>
internal sealed class NbaRequestHeadersHandler : DelegatingHandler
{
    private const string CdnHost = "cdn.nba.com";
    private const string CdnHostSuffix = ".cdn.nba.com";
    private const string ReferrerUrl = "https://www.nba.com/";
    private const string UserAgentValue =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
    private const string AcceptEncodingValue = "gzip, deflate, br";
    private static readonly Uri ReferrerUri = new(ReferrerUrl, UriKind.Absolute);

    /// <summary>
    /// Initializes a new instance of the <see cref="NbaRequestHeadersHandler"/> class.
    /// The inner handler is supplied by the pipeline.
    /// </summary>
    public NbaRequestHeadersHandler()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NbaRequestHeadersHandler"/> class
    /// chained to the supplied inner handler.
    /// </summary>
    /// <param name="innerHandler">The handler that processes the request after headers are applied.</param>
    public NbaRequestHeadersHandler(HttpMessageHandler innerHandler)
    {
        InnerHandler = innerHandler;
    }

    /// <summary>
    /// Determines whether the supplied request URI targets the NBA CDN and should
    /// therefore receive the client-hint headers.
    /// </summary>
    /// <param name="requestUri">The absolute request URI to classify.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="requestUri"/> targets
    /// <c>cdn.nba.com</c> or any subdomain of it; otherwise <see langword="false"/>.
    /// </returns>
    public static bool IsCdnRequest(Uri requestUri)
    {
        ArgumentNullException.ThrowIfNull(requestUri);

        string host = requestUri.Host;

        // Match the CDN apex exactly and any subdomain via a leading-dot suffix.
        // A bare suffix/Contains on the whole host would wrongly accept hosts such
        // as "notcdn.nba.com", so only the exact host or the ".cdn.nba.com" suffix match.
        return host.Equals(CdnHost, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(CdnHostSuffix, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ApplyBaseHeaders(request);

        if (request.RequestUri is { } requestUri && IsCdnRequest(requestUri))
        {
            ApplyCdnHeaders(request);
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static void ApplyBaseHeaders(HttpRequestMessage request)
    {
        request.Headers.UserAgent.ParseAdd(UserAgentValue);
        request.Headers.Referrer = ReferrerUri;
        request.Headers.AcceptEncoding.ParseAdd(AcceptEncodingValue);
    }

    private static void ApplyCdnHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation(
            "Sec-Ch-Ua",
            "\"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\", \"Google Chrome\";v=\"131\"");
        request.Headers.TryAddWithoutValidation("Sec-Ch-Ua-Platform", "\"Windows\"");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "none");
    }
}
