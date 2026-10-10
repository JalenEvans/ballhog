using Ballhog.Caching;
using Ballhog.Tests.Infrastructure;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Ballhog.Tests.Caching;

/// <summary>
/// Behavior tests for <see cref="BallhogCache"/> (requirement R5, Jira BALLHOG-12, chunk
/// C-006). The cache serves fresh entries without re-invoking the factory, re-fetches after
/// the TTL elapses, never caches failures, and collapses concurrent misses onto a single
/// factory execution. All timekeeping is driven by <see cref="FakeTimeProvider"/>.
/// </summary>
public sealed class BallhogCacheTests
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task GetOrAddAsync_invokes_the_factory_once_and_returns_its_value()
    {
        var time = new FakeTimeProvider();
        var cache = new BallhogCache(time, DefaultTtl);
        int factoryCalls = 0;

        IReadOnlyList<Team> value = await cache.GetOrAddAsync(
            CacheKeys.Teams,
            _ =>
            {
                Interlocked.Increment(ref factoryCalls);
                return Task.FromResult<IReadOnlyList<Team>>(TestTeams.All);
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(TestTeams.All, value);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrAddAsync_serves_a_cached_value_without_reinvoking_the_factory()
    {
        var time = new FakeTimeProvider();
        var cache = new BallhogCache(time, DefaultTtl);
        int factoryCalls = 0;
        Func<CancellationToken, Task<IReadOnlyList<Team>>> factory = _ =>
        {
            Interlocked.Increment(ref factoryCalls);
            return Task.FromResult<IReadOnlyList<Team>>(TestTeams.All);
        };

        IReadOnlyList<Team> first = await cache.GetOrAddAsync(
            CacheKeys.Teams, factory, cancellationToken: TestContext.Current.CancellationToken);
        IReadOnlyList<Team> second = await cache.GetOrAddAsync(
            CacheKeys.Teams, factory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(TestTeams.All, first);
        Assert.Equal(TestTeams.All, second);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrAddAsync_reinvokes_the_factory_after_the_default_ttl_elapses()
    {
        var time = new FakeTimeProvider();
        var cache = new BallhogCache(time, DefaultTtl);
        int factoryCalls = 0;
        Func<CancellationToken, Task<IReadOnlyList<Team>>> factory = _ =>
        {
            Interlocked.Increment(ref factoryCalls);
            return Task.FromResult<IReadOnlyList<Team>>(TestTeams.All);
        };

        await cache.GetOrAddAsync(CacheKeys.Teams, factory, cancellationToken: TestContext.Current.CancellationToken);

        time.Advance(DefaultTtl + TimeSpan.FromMilliseconds(1));

        await cache.GetOrAddAsync(CacheKeys.Teams, factory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, factoryCalls);
    }

    [Fact]
    public async Task GetOrAddAsync_does_not_reinvoke_the_factory_just_inside_the_default_ttl()
    {
        var time = new FakeTimeProvider();
        var cache = new BallhogCache(time, DefaultTtl);
        int factoryCalls = 0;
        Func<CancellationToken, Task<IReadOnlyList<Team>>> factory = _ =>
        {
            Interlocked.Increment(ref factoryCalls);
            return Task.FromResult<IReadOnlyList<Team>>(TestTeams.All);
        };

        await cache.GetOrAddAsync(CacheKeys.Teams, factory, cancellationToken: TestContext.Current.CancellationToken);

        time.Advance(DefaultTtl - TimeSpan.FromMilliseconds(1));

        await cache.GetOrAddAsync(CacheKeys.Teams, factory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrAddAsync_honors_a_per_entry_ttl_override()
    {
        var time = new FakeTimeProvider();
        var cache = new BallhogCache(time, DefaultTtl);
        int factoryCalls = 0;
        Func<CancellationToken, Task<IReadOnlyList<Team>>> factory = _ =>
        {
            Interlocked.Increment(ref factoryCalls);
            return Task.FromResult<IReadOnlyList<Team>>(TestTeams.All);
        };
        TimeSpan overrideTtl = TimeSpan.FromSeconds(1);

        await cache.GetOrAddAsync(
            CacheKeys.Teams, factory, overrideTtl, TestContext.Current.CancellationToken);

        // Past the per-entry TTL but well inside the (longer) default TTL.
        time.Advance(TimeSpan.FromSeconds(2));

        await cache.GetOrAddAsync(
            CacheKeys.Teams, factory, overrideTtl, TestContext.Current.CancellationToken);

        Assert.Equal(2, factoryCalls);
    }

    [Fact]
    public async Task GetOrAddAsync_does_not_cache_a_failing_factory()
    {
        var time = new FakeTimeProvider();
        var cache = new BallhogCache(time, DefaultTtl);
        int factoryCalls = 0;
        Func<CancellationToken, Task<IReadOnlyList<Team>>> factory = _ =>
        {
            int call = Interlocked.Increment(ref factoryCalls);
            return call == 1
                ? Task.FromException<IReadOnlyList<Team>>(new InvalidOperationException("source down"))
                : Task.FromResult<IReadOnlyList<Team>>(TestTeams.All);
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cache.GetOrAddAsync(CacheKeys.Teams, factory, cancellationToken: TestContext.Current.CancellationToken));

        IReadOnlyList<Team> second = await cache.GetOrAddAsync(
            CacheKeys.Teams, factory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(TestTeams.All, second);
        Assert.Equal(2, factoryCalls);
    }

    [Fact]
    public async Task GetOrAddAsync_collapses_concurrent_misses_onto_one_factory_execution()
    {
        var time = new FakeTimeProvider();
        var cache = new BallhogCache(time, DefaultTtl);
        int factoryCalls = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<CancellationToken, Task<IReadOnlyList<Team>>> factory = async _ =>
        {
            Interlocked.Increment(ref factoryCalls);
            started.TrySetResult();
            await gate.Task;
            return TestTeams.All;
        };

        Task<IReadOnlyList<Team>>[] inflight =
        [
            .. Enumerable.Range(0, 8).Select(_ =>
                cache.GetOrAddAsync(CacheKeys.Teams, factory, cancellationToken: TestContext.Current.CancellationToken)),
        ];

        // Wait until the single shared factory has started, without polling a counter.
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        gate.SetResult();

        // Await the aggregation itself: this follows the full continuation chain, including
        // the thread-pool scheduling hop after SetResult, with no wall-clock poll budget.
        IReadOnlyList<Team>[] results = await Task.WhenAll(inflight).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(1, factoryCalls);
        Assert.All(results, result => Assert.Equal(TestTeams.All, result));
    }
}
