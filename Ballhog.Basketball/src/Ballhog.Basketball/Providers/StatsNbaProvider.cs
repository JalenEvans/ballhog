using System.Globalization;
using System.Text.Json;
using Ballhog.Basketball.Http;

namespace Ballhog.Basketball.Providers;

/// <summary>
/// The <see cref="IStatsProvider"/> implementation backed by the NBA Stats API endpoints.
/// </summary>
/// <remarks>
/// This provider owns the mapping from raw NBA Stats responses to the public
/// <see cref="Team"/> model. It is internal so transport and mapping details never leak into
/// the public contract; tests exercise it through <c>InternalsVisibleTo("Ballhog.Tests")</c>.
/// </remarks>
internal sealed class StatsNbaProvider : IStatsProvider
{
    /// <summary>The Stats NBA <c>commonteamyears</c> endpoint queried for the team set.</summary>
    private const string CommonTeamYearsEndpoint = "https://stats.nba.com/stats/commonteamyears";

    /// <summary>The NBA league identifier for the top league, emitted as <c>LeagueID=00</c>.</summary>
    private const string LeagueId = "00";

    /// <summary>
    /// The earliest latest-season year a team is considered active. Rows whose
    /// <c>MAX_YEAR</c> predates this value are historical franchises (for example SEA, VAN,
    /// NJN) and are excluded even when they reuse a currently active franchise id.
    /// </summary>
    private const int FirstActiveSeasonYear = 2024;

    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="StatsNbaProvider"/> class.
    /// </summary>
    /// <param name="httpClient">
    /// The HTTP client used for outbound NBA Stats requests. It is injected so the provider
    /// never relies on a static client and remains fully testable.
    /// </param>
    public StatsNbaProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Team>> GetTeamsAsync(CancellationToken cancellationToken = default)
    {
        // Cancellation must win before any transport work is attempted.
        cancellationToken.ThrowIfCancellationRequested();

        Uri requestUri = new($"{CommonTeamYearsEndpoint}?{NbaQueryStringBuilder.Build(LeagueId)}");

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

        // The injected client is owned by the caller (DI), so it is never disposed here.
        using HttpResponseMessage response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // Disposing the response (below) disposes its content as well.
            throw new BallhogHttpException(
                $"The NBA Stats commonteamyears request failed with HTTP {(int)response.StatusCode} ({response.StatusCode}).",
                response.StatusCode);
        }

        using Stream stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        TeamYearsResponse? payload = await JsonSerializer
            .DeserializeAsync(stream, BallhogJsonContext.Default.TeamYearsResponse, cancellationToken)
            .ConfigureAwait(false);

        // The resultSets member is shape-inconsistent (v3 object vs v2-style single-element
        // array); normalize it to the single result-set object before mapping. A null payload
        // or an absent/empty result set yields an empty team list.
        return payload is null
            ? Array.Empty<Team>()
            : MapTeams(NormalizeResultSets(payload.ResultSets));
    }

    /// <summary>
    /// Normalizes the response's <c>resultSets</c> member to the single result-set object.
    /// </summary>
    /// <param name="resultSets">The raw member value, or <see langword="null"/> when absent.</param>
    /// <returns>
    /// The result-set object when the member is an object, its first element when it is a
    /// non-empty v2-style array, or <see langword="default"/> (an <see cref="JsonValueKind.Undefined"/>
    /// element) when the member is absent, an empty array, or any other shape.
    /// </returns>
    private static JsonElement NormalizeResultSets(JsonElement? resultSets)
    {
        if (resultSets is not { } element)
        {
            return default;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Object => element,
            // v2-style: a single-element array holding the one result set. An empty array
            // carries no result set, which the mapper treats as an empty team list.
            JsonValueKind.Array when element.GetArrayLength() > 0 => element[0],
            _ => default,
        };
    }

    /// <summary>
    /// Projects a parsed <c>commonteamyears</c> result set onto the active teams, sorted
    /// ascending by <see cref="Team.TeamId"/>.
    /// </summary>
    /// <param name="resultSets">The normalized result-set object, or <see langword="default"/>.</param>
    /// <returns>
    /// The active teams, excluding historical rows whose <c>MAX_YEAR</c> predates
    /// <see cref="FirstActiveSeasonYear"/>. An empty list is returned when the response
    /// carries no usable result set.
    /// </returns>
    private static IReadOnlyList<Team> MapTeams(JsonElement resultSets)
    {
        if (!TryExtractResultSet(resultSets, out string[] headers, out JsonElement[][] rows))
        {
            return Array.Empty<Team>();
        }

        int abbreviationIndex = IndexOfHeader(headers, "ABBREVIATION");
        int teamIdIndex = IndexOfHeader(headers, "TEAM_ID");
        int maxYearIndex = IndexOfHeader(headers, "MAX_YEAR");

        if (abbreviationIndex < 0 || teamIdIndex < 0 || maxYearIndex < 0)
        {
            return Array.Empty<Team>();
        }

        var teams = new List<Team>(rows.Length);

        foreach (JsonElement[] row in rows)
        {
            if (!TryReadCell(row, teamIdIndex, out JsonElement teamIdCell)
                || !TryReadCell(row, maxYearIndex, out JsonElement maxYearCell)
                || !TryGetInt32(teamIdCell, out int teamId)
                || !TryGetInt32(maxYearCell, out int maxYear))
            {
                continue;
            }

            // Historical rows are excluded by their latest season, not by id uniqueness:
            // SEA/VAN/NJN reuse the active OKC/MEM/BKN franchise ids, so the active row wins.
            if (maxYear < FirstActiveSeasonYear)
            {
                continue;
            }

            if (!TryReadCell(row, abbreviationIndex, out JsonElement abbreviationCell)
                || abbreviationCell.ValueKind != JsonValueKind.String
                || abbreviationCell.GetString() is not { Length: > 0 } abbreviation)
            {
                continue;
            }

            teams.Add(new Team(teamId, abbreviation));
        }

        teams.Sort(static (left, right) => left.TeamId.CompareTo(right.TeamId));

        return teams;
    }

    /// <summary>
    /// Extracts the <c>headers</c> and <c>rowSet</c> members from a normalized result-set
    /// object without going through a typed DTO, so both response shapes share one mapping
    /// path.
    /// </summary>
    /// <param name="resultSets">The normalized result-set object.</param>
    /// <param name="headers">The column names when extraction succeeds; otherwise empty.</param>
    /// <param name="rows">
    /// The rows when extraction succeeds; otherwise empty. Each row is an array of
    /// <see cref="JsonElement"/> cells; a non-array row is represented as an empty array so
    /// its cells simply fail to read and the row is skipped.
    /// </param>
    /// <returns><see langword="true"/> when both members were present in the expected shapes.</returns>
    private static bool TryExtractResultSet(
        JsonElement resultSets,
        out string[] headers,
        out JsonElement[][] rows)
    {
        headers = Array.Empty<string>();
        rows = Array.Empty<JsonElement[]>();

        if (resultSets.ValueKind != JsonValueKind.Object
            || !TryGetResultSetProperty(resultSets, "headers", out JsonElement headersElement)
            || headersElement.ValueKind != JsonValueKind.Array
            || !TryGetResultSetProperty(resultSets, "rowSet", out JsonElement rowSetElement)
            || rowSetElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        headers = new string[headersElement.GetArrayLength()];
        int headerIndex = 0;
        foreach (JsonElement header in headersElement.EnumerateArray())
        {
            headers[headerIndex++] = header.ValueKind == JsonValueKind.String
                ? header.GetString() ?? string.Empty
                : header.ToString();
        }

        rows = new JsonElement[rowSetElement.GetArrayLength()][];
        int rowIndex = 0;
        foreach (JsonElement row in rowSetElement.EnumerateArray())
        {
            rows[rowIndex++] = row.ValueKind == JsonValueKind.Array
                ? row.EnumerateArray().ToArray()
                : Array.Empty<JsonElement>();
        }

        return true;
    }

    /// <summary>
    /// Reads a property from a result-set object by its pinned name, falling back to an
    /// ordinal-ignore-case match so a casing variation in the source does not break mapping.
    /// </summary>
    /// <param name="element">The result-set object.</param>
    /// <param name="name">The pinned property name.</param>
    /// <param name="value">The property value when found; otherwise <see langword="default"/>.</param>
    /// <returns><see langword="true"/> when the property was found; otherwise <see langword="false"/>.</returns>
    private static bool TryGetResultSetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Locates a column by name in a v3 <c>headers</c> array.
    /// </summary>
    /// <param name="headers">The column names, or <see langword="null"/> when absent.</param>
    /// <param name="name">The column name to find, matched case-insensitively.</param>
    /// <returns>The zero-based column index, or <c>-1</c> when the column is not present.</returns>
    private static int IndexOfHeader(string[]? headers, string name)
    {
        if (headers is null)
        {
            return -1;
        }

        for (int index = 0; index < headers.Length; index++)
        {
            if (string.Equals(headers[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Reads a single cell from a row when its column index is in range.
    /// </summary>
    /// <param name="row">The row being mapped.</param>
    /// <param name="index">The zero-based column index to read.</param>
    /// <param name="cell">The cell value when read successfully; otherwise <see langword="default"/>.</param>
    /// <returns><see langword="true"/> when the index is in range; otherwise <see langword="false"/>.</returns>
    private static bool TryReadCell(JsonElement[] row, int index, out JsonElement cell)
    {
        if (index >= 0 && index < row.Length)
        {
            cell = row[index];
            return true;
        }

        cell = default;
        return false;
    }

    /// <summary>
    /// Reads an integer cell that the source may encode either as a JSON number or as a
    /// string (the Stats API is inconsistent about this for id and year columns).
    /// </summary>
    /// <param name="cell">The cell value to read.</param>
    /// <param name="value">The parsed integer when conversion succeeds; otherwise <c>0</c>.</param>
    /// <returns><see langword="true"/> when the cell was an integer; otherwise <see langword="false"/>.</returns>
    private static bool TryGetInt32(JsonElement cell, out int value)
    {
        switch (cell.ValueKind)
        {
            case JsonValueKind.Number:
                return cell.TryGetInt32(out value);
            case JsonValueKind.String:
                return int.TryParse(
                    cell.GetString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value);
            default:
                value = 0;
                return false;
        }
    }
}
