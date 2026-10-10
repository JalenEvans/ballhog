using System.Globalization;
using System.Text.Json;
using Ballhog.Http;

namespace Ballhog.Providers;

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

        return payload is null ? Array.Empty<Team>() : MapTeams(payload);
    }

    /// <summary>
    /// Projects a parsed <c>commonteamyears</c> payload onto the active teams, sorted
    /// ascending by <see cref="Team.TeamId"/>.
    /// </summary>
    /// <param name="payload">The parsed response body.</param>
    /// <returns>
    /// The active teams, excluding historical rows whose <c>MAX_YEAR</c> predates
    /// <see cref="FirstActiveSeasonYear"/>. An empty list is returned when the response
    /// carries no usable result set.
    /// </returns>
    private static IReadOnlyList<Team> MapTeams(TeamYearsResponse payload)
    {
        if (payload.ResultSets is not { RowSet: { } rows } resultSets)
        {
            return Array.Empty<Team>();
        }

        int abbreviationIndex = IndexOfHeader(resultSets.Headers, "ABBREVIATION");
        int teamIdIndex = IndexOfHeader(resultSets.Headers, "TEAM_ID");
        int maxYearIndex = IndexOfHeader(resultSets.Headers, "MAX_YEAR");

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
