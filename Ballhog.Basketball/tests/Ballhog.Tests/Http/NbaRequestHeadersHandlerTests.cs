using System.Net;
using System.Net.Http;
using Ballhog.Basketball.Http;
using Ballhog.Tests.Infrastructure;
using Xunit;

namespace Ballhog.Tests.Http;

/// <summary>
/// Behavior tests for <see cref="NbaRequestHeadersHandler"/> (requirement R4, chunk C-004).
/// The handler must stamp the 2026 NBA request headers onto every outgoing request and
/// add the client-hint headers only for NBA CDN traffic, while never setting <c>Host</c>.
/// </summary>
public sealed class NbaRequestHeadersHandlerTests
{
    [Theory]
    [InlineData("https://cdn.nba.com/static/json/liveData/scoreboard/todaysScoreboard_00.json", true)]
    [InlineData("https://x.cdn.nba.com/static/json/example.json", true)]
    [InlineData("https://stats.nba.com/stats/leaguedashteamstats", false)]
    [InlineData("https://www.nba.com/", false)]
    [InlineData("https://notcdn.nba.com/static/json/example.json", false)]
    public void IsCdnRequest_classifies_nba_hosts(string url, bool expected)
    {
        Assert.Equal(expected, NbaRequestHeadersHandler.IsCdnRequest(new Uri(url)));
    }

    [Fact]
    public async Task Every_request_receives_the_base_headers()
    {
        using HttpRequestMessage request = await CaptureRequestAsync("https://stats.nba.com/stats/leaguedashteamstats");

        string userAgent = request.Headers.UserAgent.ToString();
        Assert.Contains("Mozilla/5.0", userAgent, StringComparison.Ordinal);
        Assert.Contains("Chrome/", userAgent, StringComparison.Ordinal);

        Assert.Equal(new Uri("https://www.nba.com/"), request.Headers.Referrer);
        Assert.Contains("gzip", request.Headers.AcceptEncoding.ToString(), StringComparison.OrdinalIgnoreCase);

        // .NET derives Host from the request URI; the handler must not set it.
        Assert.Null(request.Headers.Host);
    }

    [Theory]
    [InlineData("https://cdn.nba.com/static/json/liveData/scoreboard/todaysScoreboard_00.json")]
    [InlineData("https://x.cdn.nba.com/static/json/example.json")]
    public async Task Cdn_requests_receive_the_client_hint_headers(string url)
    {
        using HttpRequestMessage request = await CaptureRequestAsync(url);

        Assert.True(request.Headers.Contains("Sec-Ch-Ua"));
        Assert.True(request.Headers.Contains("Sec-Ch-Ua-Platform"));
        Assert.True(request.Headers.Contains("Sec-Fetch-Mode"));
        Assert.True(request.Headers.Contains("Sec-Fetch-Site"));
    }

    [Theory]
    [InlineData("https://stats.nba.com/stats/leaguedashteamstats")]
    [InlineData("https://www.nba.com/")]
    public async Task Non_cdn_requests_do_not_receive_the_client_hint_headers(string url)
    {
        using HttpRequestMessage request = await CaptureRequestAsync(url);

        Assert.False(request.Headers.Contains("Sec-Ch-Ua"));
        Assert.False(request.Headers.Contains("Sec-Ch-Ua-Platform"));
        Assert.False(request.Headers.Contains("Sec-Fetch-Mode"));
        Assert.False(request.Headers.Contains("Sec-Fetch-Site"));
    }

    private static async Task<HttpRequestMessage> CaptureRequestAsync(string url)
    {
        var inner = new ScriptedHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var handler = new NbaRequestHeadersHandler(inner);
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: true);

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        using HttpResponseMessage response = await invoker.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return request;
    }
}
