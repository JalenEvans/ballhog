using System.Text;

namespace Ballhog.Basketball.Http;

/// <summary>
/// Builds NBA query strings in the canonical order required by the source endpoints:
/// <c>LeagueID</c> first, followed by the remaining parameters in a deterministic order.
/// </summary>
/// <remarks>
/// The builder is a pure, side-effect-free helper so it can be exercised directly in tests
/// across the <c>season</c>, <c>date</c>, <c>measure</c>, and <c>id</c> request variants
/// without any network access.
/// </remarks>
internal static class NbaQueryStringBuilder
{
    /// <summary>
    /// Builds a query string with <c>LeagueID</c> as the first parameter, followed by the
    /// remaining key/value pairs in a deterministic order.
    /// </summary>
    /// <param name="leagueId">The league identifier emitted as the leading <c>LeagueID</c> parameter.</param>
    /// <param name="parameters">The additional query parameters; may be empty.</param>
    /// <returns>
    /// The URL-encoded query component (without a leading <c>?</c>), for example
    /// <c>LeagueID=00&amp;Season=2025-26</c>.
    /// </returns>
    /// <remarks>
    /// Parameters with a <see langword="null"/> value are omitted. Keys are ordered with
    /// <see cref="string.CompareOrdinal(string, string)"/> and every key and value is
    /// percent-encoded with <see cref="Uri.EscapeDataString(string)"/>.
    /// </remarks>
    public static string Build(string leagueId, IEnumerable<KeyValuePair<string, string?>> parameters)
    {
        ArgumentNullException.ThrowIfNull(leagueId);
        ArgumentNullException.ThrowIfNull(parameters);

        var builder = new StringBuilder();
        builder.Append("LeagueID=").Append(Uri.EscapeDataString(leagueId));

        foreach (KeyValuePair<string, string?> parameter in parameters
            .Where(static parameter => parameter.Value is not null)
            .OrderBy(static parameter => parameter.Key, StringComparer.Ordinal))
        {
            builder.Append('&')
                .Append(Uri.EscapeDataString(parameter.Key))
                .Append('=')
                .Append(Uri.EscapeDataString(parameter.Value!));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Builds a query string with <c>LeagueID</c> as the first parameter, followed by the
    /// remaining key/value pairs in a deterministic order.
    /// </summary>
    /// <param name="leagueId">The league identifier emitted as the leading <c>LeagueID</c> parameter.</param>
    /// <param name="parameters">The additional query parameters; may be empty.</param>
    /// <returns>
    /// The URL-encoded query component (without a leading <c>?</c>), for example
    /// <c>LeagueID=00&amp;Season=2025-26</c>.
    /// </returns>
    public static string Build(string leagueId, params KeyValuePair<string, string?>[] parameters)
        => Build(leagueId, (IEnumerable<KeyValuePair<string, string?>>)parameters);
}
