using Ballhog.Basketball;
using Ballhog.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Ballhog.Tests.Client;

/// <summary>
/// F1 regression tests (Offensive-Coach fix, swappable-source seam): verifies the SDK's TTL
/// cache and single-flight apply over a swapped <see cref="IStatsProvider"/> source through
/// either supported DI injection style; pacing over the decorator is covered in
/// <c>CachingStatsProviderTests</c>. These tests resolve the full <c>AddBallhogBasketball</c>
/// container offline: fake time drives TTL expiry, and a counting <see cref="FakeStatsProvider"/>
/// stands in as the swapped source.
/// </summary>
/// <remarks>
/// Both injection styles are covered for the cache-hit contract: pre-registering the source
/// before <c>AddBallhogBasketball</c> (which <c>TryAddTransient</c> then skips) and
/// <c>Replace</c>-ing it afterwards. TTL expiry and single-flight verify the shared SDK layers
/// on the resulting graph and never touch the transport (the default <see cref="StatsNbaProvider"/>
/// is never constructed).
/// </remarks>
public sealed class SwappedProviderCachingTests
{
    /// <summary>The default teams TTL from <see cref="BallhogOptions"/> (12 hours).</summary>
    private static readonly TimeSpan TeamsCacheTtl = TimeSpan.FromHours(12);

    /// <summary>The default source pacing interval from <see cref="BallhogOptions"/> (1 second).</summary>
    private static readonly TimeSpan PacingInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// An advance that clears the teams TTL (and the pacing interval) so the next call is a
    /// genuine source miss rather than a pacing-suppressed one.
    /// </summary>
    private static readonly TimeSpan PastTtlAdvance = TeamsCacheTtl + PacingInterval + TimeSpan.FromHours(1);

    [Fact]
    public async Task Replaced_provider_still_serves_a_second_call_from_cache()
    {
        var time = new FakeTimeProvider();
        var fake = new FakeStatsProvider(TestTeams.All);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddBallhogBasketball();

        // Replace-after injection style: the SDK's TryAddTransient registration is replaced
        // with the test source before the container is built.
        services.Replace(ServiceDescriptor.Singleton<IStatsProvider>(fake));

        using ServiceProvider provider = services.BuildServiceProvider();
        IBallhogClient client = provider.GetRequiredService<IBallhogClient>();

        IReadOnlyList<Team> first = await client.GetTeamsAsync(TestContext.Current.CancellationToken);
        IReadOnlyList<Team> second = await client.GetTeamsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TestTeams.All, first);
        Assert.Equal(TestTeams.All, second);

        // Both calls stayed inside the TTL, so the swapped source was hit exactly once.
        Assert.Equal(1, fake.CallCount);
    }

    [Fact]
    public async Task Pre_registered_provider_still_serves_a_second_call_from_cache()
    {
        var time = new FakeTimeProvider();
        var fake = new FakeStatsProvider(TestTeams.All);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);

        // Pre-register injection style: the source is registered before AddBallhogBasketball.
        // The SDK's TryAddTransient<IStatsProvider> then skips and the fake wins.
        services.AddSingleton<IStatsProvider>(fake);
        services.AddBallhogBasketball();

        using ServiceProvider provider = services.BuildServiceProvider();
        IBallhogClient client = provider.GetRequiredService<IBallhogClient>();

        IReadOnlyList<Team> first = await client.GetTeamsAsync(TestContext.Current.CancellationToken);
        IReadOnlyList<Team> second = await client.GetTeamsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TestTeams.All, first);
        Assert.Equal(TestTeams.All, second);

        // The cache-aside layer still sits on top of the pre-registered source.
        Assert.Equal(1, fake.CallCount);
    }

    [Fact]
    public async Task Replaced_provider_refetches_on_the_third_call_after_the_teams_ttl_elapses()
    {
        var time = new FakeTimeProvider();
        var fake = new FakeStatsProvider(TestTeams.All);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddBallhogBasketball();
        services.Replace(ServiceDescriptor.Singleton<IStatsProvider>(fake));

        using ServiceProvider provider = services.BuildServiceProvider();
        IBallhogClient client = provider.GetRequiredService<IBallhogClient>();

        IReadOnlyList<Team> first = await client.GetTeamsAsync(TestContext.Current.CancellationToken);
        IReadOnlyList<Team> second = await client.GetTeamsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(TestTeams.All, first);
        Assert.Equal(TestTeams.All, second);
        Assert.Equal(1, fake.CallCount);

        // Past the teams TTL the cache entry expires; the next call hits the swapped source
        // again (the pacer's last release is long past its interval, so this is immediate).
        time.Advance(PastTtlAdvance);

        IReadOnlyList<Team> third = await client.GetTeamsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TestTeams.All, third);
        Assert.Equal(2, fake.CallCount);
    }

    [Fact]
    public async Task Concurrent_cold_misses_collapse_onto_one_call_of_a_replaced_provider()
    {
        var time = new FakeTimeProvider();
        var fake = new FakeStatsProvider(TestTeams.All);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddBallhogBasketball();
        services.Replace(ServiceDescriptor.Singleton<IStatsProvider>(fake));

        using ServiceProvider provider = services.BuildServiceProvider();
        IBallhogClient client = provider.GetRequiredService<IBallhogClient>();

        // N parallel cold misses on a fresh container must share one factory execution.
        Task<IReadOnlyList<Team>>[] inflight =
        [
            .. Enumerable.Range(0, 8).Select(_ =>
                client.GetTeamsAsync(TestContext.Current.CancellationToken)),
        ];

        IReadOnlyList<Team>[] results = await Task.WhenAll(inflight).WaitAsync(
            TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(1, fake.CallCount);
        Assert.All(results, result => Assert.Equal(TestTeams.All, result));
    }
}
