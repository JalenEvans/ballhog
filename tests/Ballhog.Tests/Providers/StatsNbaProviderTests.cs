using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using Ballhog.Http;
using Ballhog.Providers;
using Ballhog.Tests.Infrastructure;
using Xunit;

namespace Ballhog.Tests.Providers;

/// <summary>
/// Behavior tests for <see cref="StatsNbaProvider"/> (requirement R3, Jira BALLHOG-10,
/// chunk C-005). The provider issues a GET against the Stats NBA <c>commonteamyears</c>
/// endpoint, maps the v3 result set onto <see cref="Team"/>, keeps only the 30 currently
/// active franchises, and returns them sorted ascending by <see cref="Team.TeamId"/>.
/// </summary>
/// <remarks>
/// Every test here is offline: the response body is the pinned
/// <c>Fixtures/teams.commonteamyears.json</c> sample and transport is driven by
/// <see cref="ScriptedHttpMessageHandler"/>, so the suite exercises provider behavior
/// without a network. The fixture is a documented sample (not yet captured live); live
/// parity is deferred to a <c>Category=Live</c> test.
/// </remarks>
public sealed class StatsNbaProviderTests
{
    private const string CommonTeamYearsFixture = "teams.commonteamyears";

    /// <summary>
    /// The same <c>commonteamyears</c> rows wrapped in the v2-style single-element
    /// <c>resultSets</c> array instead of the v3 object, used to prove both shapes parse
    /// identically.
    /// </summary>
    private const string CommonTeamYearsArrayFixture = "teams.commonteamyears.array";

    /// <summary>
    /// The 30 active franchises in the exact order the provider must return them:
    /// ascending by <see cref="Team.TeamId"/>. The historical rows (SEA, VAN, NJN) are
    /// intentionally absent.
    /// </summary>
    private static readonly Team[] ExpectedTeams =
    [
        new(1610612737, "ATL"),
        new(1610612738, "BOS"),
        new(1610612739, "CLE"),
        new(1610612740, "NOP"),
        new(1610612741, "CHI"),
        new(1610612742, "DAL"),
        new(1610612743, "DEN"),
        new(1610612744, "GSW"),
        new(1610612745, "HOU"),
        new(1610612746, "LAC"),
        new(1610612747, "LAL"),
        new(1610612748, "MIA"),
        new(1610612749, "MIL"),
        new(1610612750, "MIN"),
        new(1610612751, "BKN"),
        new(1610612752, "NYK"),
        new(1610612753, "ORL"),
        new(1610612754, "IND"),
        new(1610612755, "PHI"),
        new(1610612756, "PHX"),
        new(1610612757, "POR"),
        new(1610612758, "SAC"),
        new(1610612759, "SAS"),
        new(1610612760, "OKC"),
        new(1610612761, "TOR"),
        new(1610612762, "UTA"),
        new(1610612763, "MEM"),
        new(1610612764, "WAS"),
        new(1610612765, "DET"),
        new(1610612766, "CHA"),
    ];

    [Fact]
    public async Task GetTeamsAsync_performs_a_get_to_the_common_team_years_endpoint()
    {
        (StatsNbaProvider provider, ScriptedHttpMessageHandler handler) = CreateProvider(SuccessResponse());

        _ = await provider.GetTeamsAsync(TestContext.Current.CancellationToken);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("stats.nba.com", request.RequestUri!.Host);
        Assert.Equal("/stats/commonteamyears", request.RequestUri.AbsolutePath);
    }

    [Fact]
    public async Task GetTeamsAsync_returns_exactly_the_30_active_teams_excluding_historical_rows()
    {
        (StatsNbaProvider provider, _) = CreateProvider(SuccessResponse());

        IReadOnlyList<Team> teams = await provider.GetTeamsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ExpectedTeams.Length, teams.Count);
        Assert.Equal(ExpectedTeams.Length, teams.Select(team => team.TeamId).Distinct().Count());
        Assert.DoesNotContain(teams, team => team.Abbreviation is "SEA" or "VAN" or "NJN");
    }

    [Fact]
    public async Task GetTeamsAsync_sorts_teams_ascending_by_team_id()
    {
        (StatsNbaProvider provider, _) = CreateProvider(SuccessResponse());

        IReadOnlyList<Team> teams = await provider.GetTeamsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ExpectedTeams[0], teams[0]);
        Assert.Equal(ExpectedTeams[^1], teams[^1]);
        Assert.Equal(
            teams.Select(team => team.TeamId),
            teams.Select(team => team.TeamId).Order());
    }

    [Fact]
    public async Task GetTeamsAsync_maps_each_row_to_a_team()
    {
        (StatsNbaProvider provider, _) = CreateProvider(SuccessResponse());

        IReadOnlyList<Team> teams = await provider.GetTeamsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ExpectedTeams, teams);

        // Spot checks. Note the historical SEA row reuses OKC's franchise id
        // (1610612760): the active row must win, so OKC (not SEA) is present.
        Assert.Contains(new Team(1610612737, "ATL"), teams);
        Assert.Contains(new Team(1610612760, "OKC"), teams);
    }

    /// <summary>
    /// The endpoint is shape-inconsistent about <c>resultSets</c>: the documented v3 form is a
    /// single object, while some responses emit a v2-style single-element array. This proves
    /// the array form maps to exactly the same 30 active teams, in the same order, as the
    /// object form.
    /// </summary>
    [Fact]
    public async Task GetTeamsAsync_parses_the_v2_style_array_resultSets_identically_to_the_object_form()
    {
        (StatsNbaProvider arrayProvider, _) = CreateProvider(ArrayResponse());
        (StatsNbaProvider objectProvider, _) = CreateProvider(SuccessResponse());

        IReadOnlyList<Team> arrayTeams = await arrayProvider.GetTeamsAsync(TestContext.Current.CancellationToken);
        IReadOnlyList<Team> objectTeams = await objectProvider.GetTeamsAsync(TestContext.Current.CancellationToken);

        // Exactly the 30 active franchises, sorted ascending by id, historical rows excluded.
        Assert.Equal(30, arrayTeams.Count);
        Assert.Equal(ExpectedTeams, arrayTeams);
        Assert.Contains(new Team(1610612737, "ATL"), arrayTeams);
        Assert.Contains(new Team(1610612760, "OKC"), arrayTeams);

        // The v2-style array parses to the exact same sequence as the v3 object form.
        Assert.True(
            arrayTeams.SequenceEqual(objectTeams),
            "The array-form resultSets must parse identically to the object form.");
    }

    [Fact]
    public async Task GetTeamsAsync_throws_BallhogHttpException_on_non_success_response()
    {
        (StatsNbaProvider provider, ScriptedHttpMessageHandler handler) = CreateProvider(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        BallhogHttpException exception = await Assert.ThrowsAsync<BallhogHttpException>(
            () => provider.GetTeamsAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetTeamsAsync_throws_OperationCanceledException_for_a_canceled_token()
    {
        (StatsNbaProvider provider, ScriptedHttpMessageHandler handler) = CreateProvider(SuccessResponse());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetTeamsAsync(cancellation.Token));

        // Cancellation must win before any transport work is attempted.
        Assert.Equal(0, handler.CallCount);
    }

    /// <summary>
    /// Returns an <see cref="HttpStatusCode.OK"/> response whose body is the pinned
    /// <c>commonteamyears</c> fixture. The fixture is JSONC: the provenance comments
    /// are the human aid, and <see cref="FixtureLoader"/> tolerates trailing commas too;
    /// the node is re-emitted as strict JSON so the provider only ever sees what the real
    /// Stats API would send.
    /// </summary>
    private static HttpResponseMessage SuccessResponse()
    {
        string json = FixtureLoader.Load<JsonObject>(CommonTeamYearsFixture).ToJsonString();

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>
    /// Returns an <see cref="HttpStatusCode.OK"/> response carrying the v2-style
    /// <c>resultSets</c>-array fixture, re-emitted as strict JSON exactly like
    /// <see cref="SuccessResponse"/>.
    /// </summary>
    private static HttpResponseMessage ArrayResponse()
    {
        string json = FixtureLoader.Load<JsonObject>(CommonTeamYearsArrayFixture).ToJsonString();

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private static (StatsNbaProvider Provider, ScriptedHttpMessageHandler Handler) CreateProvider(
        params HttpResponseMessage[] responses)
    {
        var handler = new ScriptedHttpMessageHandler(responses);
        return (new StatsNbaProvider(new HttpClient(handler)), handler);
    }
}
