using Ballhog.Basketball;
using Ballhog.Basketball.Caching;
using Ballhog.Tests.Infrastructure;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Ballhog.Tests.Caching;

/// <summary>
/// Behavior tests for <see cref="CachingStatsProvider"/> (requirement R5, Jira BALLHOG-12,
/// chunk C-006). The decorator serves fresh teams from cache, re-fetches after the teams TTL,
/// never caches source failures, collapses concurrent cold misses onto one source call, and
/// paces source traffic through the shared <see cref="SourcePacer"/>.
/// </summary>
public sealed class CachingStatsProviderTests
{
    private static readonly TimeSpan TeamsTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task GetTeamsAsync_serves_the_second_call_from_cache_without_hitting_the_source()
    {
        var time = new FakeTimeProvider();
        var inner = new FakeStatsProvider(TestTeams.All);
        CachingStatsProvider provider = Create(time, inner);

        IReadOnlyList<Team> first = await provider.GetTeamsAsync(TestContext.Current.CancellationToken);
        IReadOnlyList<Team> second = await provider.GetTeamsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TestTeams.All, first);
        Assert.Equal(TestTeams.All, second);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task GetTeamsAsync_refetches_after_the_teams_ttl_elapses()
    {
        var time = new FakeTimeProvider();
        var inner = new FakeStatsProvider(TestTeams.All);
        CachingStatsProvider provider = Create(time, inner);

        await provider.GetTeamsAsync(TestContext.Current.CancellationToken);

        // Advance past the TTL (and comfortably past the pacing interval).
        time.Advance(TeamsTtl + MinInterval);

        IReadOnlyList<Team> refreshed = await provider.GetTeamsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TestTeams.All, refreshed);
        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task GetTeamsAsync_does_not_refetch_just_inside_the_teams_ttl()
    {
        var time = new FakeTimeProvider();
        var inner = new FakeStatsProvider(TestTeams.All);
        CachingStatsProvider provider = Create(time, inner);

        await provider.GetTeamsAsync(TestContext.Current.CancellationToken);

        time.Advance(TeamsTtl - TimeSpan.FromMilliseconds(1));

        IReadOnlyList<Team> cached = await provider.GetTeamsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TestTeams.All, cached);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task GetTeamsAsync_propagates_and_does_not_cache_a_source_failure()
    {
        var time = new FakeTimeProvider();
        var inner = new FakeStatsProvider((call, _) => call == 1
            ? throw new InvalidOperationException("source unavailable")
            : Task.FromResult<IReadOnlyList<Team>>(TestTeams.All));
        CachingStatsProvider provider = Create(time, inner);

        Task<IReadOnlyList<Team>> firstAttempt = provider.GetTeamsAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => firstAttempt.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        // The failure must not have been cached: the next call hits the source again. The
        // shared pacer makes that second source call wait out one interval of fake time.
        Task<IReadOnlyList<Team>> secondAttempt = provider.GetTeamsAsync(TestContext.Current.CancellationToken);
        time.Advance(MinInterval);
        IReadOnlyList<Team> second = await secondAttempt.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(TestTeams.All, second);
        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task GetTeamsAsync_collapses_concurrent_cold_misses_onto_one_source_call()
    {
        var time = new FakeTimeProvider();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new FakeStatsProvider(async (_, _) =>
        {
            started.TrySetResult();
            await gate.Task;
            return TestTeams.All;
        });
        CachingStatsProvider provider = Create(time, inner);

        Task<IReadOnlyList<Team>>[] inflight =
        [
            .. Enumerable.Range(0, 8).Select(_ =>
                provider.GetTeamsAsync(TestContext.Current.CancellationToken)),
        ];

        // Wait until the single shared factory reaches the gated source call.
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        gate.SetResult();

        // Await the aggregation itself: this follows the full continuation chain, including
        // the thread-pool scheduling hop after SetResult, with no wall-clock poll budget.
        Task<IReadOnlyList<Team>[]> all = Task.WhenAll(inflight);
        Task completed = await Task.WhenAny(
            all,
            Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.True(ReferenceEquals(completed, all), "single-flight aggregation did not complete within 10s");
        IReadOnlyList<Team>[] results = await all; // rethrows if any caller faulted

        Assert.Equal(1, inner.CallCount);
        Assert.All(results, result => Assert.Equal(TestTeams.All, result));
    }

    [Fact]
    public async Task GetTeamsAsync_spaces_source_calls_globally_across_cold_caches()
    {
        var time = new FakeTimeProvider();
        var pacer = new SourcePacer(time, MinInterval);
        var innerA = new FakeStatsProvider(TestTeams.All);
        var innerB = new FakeStatsProvider(TestTeams.All);
        CachingStatsProvider providerA = Create(time, innerA, pacer);
        CachingStatsProvider providerB = Create(time, innerB, pacer);

        await providerA.GetTeamsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, innerA.CallCount);

        // A miss for a different key (separate cache/provider) still shares the pacer.
        Task<IReadOnlyList<Team>> second = providerB.GetTeamsAsync(TestContext.Current.CancellationToken);

        time.Advance(MinInterval - TimeSpan.FromMilliseconds(1));
        await Task.Yield();
        Assert.False(second.IsCompleted);
        Assert.Equal(0, innerB.CallCount);

        time.Advance(TimeSpan.FromMilliseconds(2));
        await second.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(1, innerB.CallCount);
    }

    private static CachingStatsProvider Create(
        FakeTimeProvider time,
        FakeStatsProvider inner,
        SourcePacer? pacer = null,
        TimeSpan? teamsTtl = null)
    {
        TimeSpan ttl = teamsTtl ?? TeamsTtl;
        return new CachingStatsProvider(
            inner,
            new BallhogCache(time, ttl),
            pacer ?? new SourcePacer(time, MinInterval),
            ttl);
    }
}
