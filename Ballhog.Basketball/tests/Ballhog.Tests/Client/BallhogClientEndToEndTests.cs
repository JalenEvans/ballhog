using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Ballhog.Basketball;
using Ballhog.Basketball.Caching;
using Ballhog.Basketball.Providers;
using Ballhog.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Ballhog.Tests.Client;

/// <summary>
/// End-to-end composition tests for the public client (requirement R6, Jira BALLHOG-13,
/// chunk C-007; tests T-028, T-029, and T-030). Where the per-layer suites exercise each
/// collaborator in isolation, these pin the contract the consumer actually gets when the
/// real <see cref="StatsNbaProvider"/>, <see cref="CachingStatsProvider"/>,
/// <see cref="BallhogCache"/>, <see cref="SourcePacer"/>, and <see cref="BallhogClient"/>
/// are assembled into one chain.
/// </summary>
/// <remarks>
/// The offline tests (T-028, T-029) build that chain by hand over
/// <see cref="ScriptedHttpMessageHandler"/> and <see cref="FakeTimeProvider"/>: no DI,
/// no network, no real sleeps. The opt-in live test (T-030) instead drives the real
/// <c>AddBallhogBasketball</c> container against the live Stats NBA API and is excluded from every
/// ordinary run by the default <c>Category!=Live</c> filter and by
/// <see cref="LiveTestGate.SkipUnlessEnabled"/>.
/// </remarks>
public sealed class BallhogClientEndToEndTests
{
    private const string CommonTeamYearsFixture = "teams.commonteamyears";
    private static readonly TimeSpan TeamsTtl = TimeSpan.FromHours(12);
    private static readonly TimeSpan PacingInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// T-028 (offline): the full hand-built provider chain yields the 30 active franchises
    /// from the real <c>commonteamyears</c> body, sorted ascending by
    /// <see cref="Team.TeamId"/>, with the historical rows excluded.
    /// </summary>
    [Fact]
    public async Task GetTeamsAsync_returns_the_30_active_teams_through_the_real_provider_chain()
    {
        var time = new FakeTimeProvider();
        (BallhogClient client, _) = CreateOfflineClient(time);

        IReadOnlyList<Team> teams = await client.GetTeamsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(30, teams.Count);
        Assert.Equal(30, teams.Select(team => team.TeamId).Distinct().Count());

        // Sorted ascending by stable franchise id.
        Assert.Equal(
            teams.Select(team => team.TeamId),
            teams.Select(team => team.TeamId).Order());

        // Active rows win over the historical rows that reuse their ids: OKC's active row
        // (1610612760) must be present, and SEA/VAN/NJN must not survive MAX_YEAR filtering.
        Assert.Contains(new Team(1610612737, "ATL"), teams);
        Assert.Contains(new Team(1610612760, "OKC"), teams);
        Assert.DoesNotContain(teams, team => team.Abbreviation is "SEA" or "VAN" or "NJN");
    }

    /// <summary>
    /// T-029 (offline): once the first call has populated the cache, a second call inside the
    /// teams TTL is served from cache and performs no additional source request.
    /// </summary>
    [Fact]
    public async Task GetTeamsAsync_serves_a_second_call_inside_the_ttl_without_hitting_the_source()
    {
        var time = new FakeTimeProvider();
        (BallhogClient client, ScriptedHttpMessageHandler handler) = CreateOfflineClient(time);

        IReadOnlyList<Team> first = await client.GetTeamsAsync(TestContext.Current.CancellationToken);

        // Stay well inside the 12h TTL while stepping past the 1s pacing interval so a cache
        // hit cannot be confused with a pacing-suppressed miss.
        time.Advance(TimeSpan.FromSeconds(1));

        IReadOnlyList<Team> second = await client.GetTeamsAsync(TestContext.Current.CancellationToken);

        // Compare as sequences (Team[] against IReadOnlyList<Team>) so this is value equality of
        // the 30 teams, not reference equality of a single cached list instance.
        Assert.Equal(first.ToArray(), second);
        Assert.Equal(30, second.Count);
        Assert.Equal(1, handler.CallCount);
    }

    /// <summary>
    /// T-030 (live, opt-in): the real <c>AddBallhogBasketball</c> container resolves an
    /// <see cref="IBallhogClient"/> that returns the 30 active teams from the live Stats NBA
    /// API. Runs only when <c>BALLHOG_LIVE=1</c>; otherwise it reports SKIPPED.
    /// </summary>
    [Fact]
    [Trait("Category", "Live")]
    public async Task GetTeamsAsync_returns_the_30_active_teams_through_the_real_di_composition()
    {
        LiveTestGate.SkipUnlessEnabled();

        using ServiceProvider provider = new ServiceCollection().AddBallhogBasketball().BuildServiceProvider();
        IBallhogClient client = provider.GetRequiredService<IBallhogClient>();

        IReadOnlyList<Team> teams = await client.GetTeamsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(30, teams.Count);
        Assert.Equal(30, teams.Select(team => team.TeamId).Distinct().Count());
        Assert.Contains(new Team(1610612737, "ATL"), teams);
        Assert.Contains(new Team(1610612760, "OKC"), teams);
    }

    /// <summary>
    /// Assembles the production object graph without DI: the real provider decorators wrapped
    /// around <see cref="StatsNbaProvider"/> over a scripted transport, with deterministic time
    /// shared by the cache and the pacer.
    /// </summary>
    private static (BallhogClient Client, ScriptedHttpMessageHandler Handler) CreateOfflineClient(FakeTimeProvider time)
    {
        var handler = new ScriptedHttpMessageHandler(SuccessResponse());
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://stats.nba.com") };

        var provider = new StatsNbaProvider(httpClient);
        var cachingProvider = new CachingStatsProvider(
            provider,
            new BallhogCache(time, TeamsTtl),
            new SourcePacer(time, PacingInterval),
            TeamsTtl);

        return (new BallhogClient(cachingProvider), handler);
    }

    /// <summary>
    /// Returns an <see cref="HttpStatusCode.OK"/> response carrying the pinned
    /// <c>commonteamyears</c> fixture re-emitted as strict JSON (the fixture itself is JSONC
    /// for provenance, so the provider only ever sees what the real Stats API would send).
    /// </summary>
    private static HttpResponseMessage SuccessResponse()
    {
        string json = FixtureLoader.Load<JsonObject>(CommonTeamYearsFixture).ToJsonString();

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }
}
