using Ballhog.Basketball;
using Ballhog.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Ballhog.Tests.Client;

/// <summary>
/// DI-contract tests for <c>AddBallhogBasketball</c> (requirement R2, Jira BALLHOG-9, chunk C-003;
/// tests T-004, T-009/T-011, and the option-wiring cases). They pin what
/// <c>AddBallhogBasketball</c> must register: a resolvable typed <see cref="IBallhogClient"/> over a
/// swappable <see cref="IStatsProvider"/> seam, plus <see cref="BallhogOptions"/> carrying
/// either the configured overrides or the documented defaults.
/// </summary>
/// <remarks>
/// Every test here is offline: the provider seam is satisfied by the deterministic
/// <see cref="FakeStatsProvider"/>, so no transport is constructed and no network call is
/// possible. Actual HTTP wiring, cache behavior, and proxy application are deliberately out
/// of scope (chunks C-004/C-006/C-007).
/// </remarks>
public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBallhog_registers_a_resolvable_IBallhogClient()
    {
        using ServiceProvider provider = new ServiceCollection().AddBallhogBasketball().BuildServiceProvider();

        IBallhogClient client = provider.GetRequiredService<IBallhogClient>();

        Assert.NotNull(client);
        Assert.IsType<BallhogClient>(client);
    }

    [Fact]
    public async Task AddBallhog_client_reads_through_the_registered_IStatsProvider()
    {
        var fake = new FakeStatsProvider(TestTeams.All);
        var services = new ServiceCollection();
        services.AddBallhogBasketball();

        // Overriding the provider registration must redirect the client to the seam.
        services.Replace(ServiceDescriptor.Singleton<IStatsProvider>(fake));

        using ServiceProvider provider = services.BuildServiceProvider();
        IBallhogClient client = provider.GetRequiredService<IBallhogClient>();

        IReadOnlyList<Team> teams = await client.GetTeamsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TestTeams.All, teams);
        Assert.Equal(1, fake.CallCount);
    }

    [Fact]
    public void AddBallhog_applies_the_configure_callback()
    {
        var proxy = new Uri("http://localhost:8888");
        using ServiceProvider provider = new ServiceCollection()
            .AddBallhogBasketball(options =>
            {
                options.Http.Proxy = proxy;
                options.TeamsCacheTtl = TimeSpan.FromHours(2);
                options.SourcePacingInterval = TimeSpan.FromMilliseconds(500);
            })
            .BuildServiceProvider();

        BallhogOptions options = provider.GetRequiredService<BallhogOptions>();

        Assert.Equal(proxy, options.Http.Proxy);
        Assert.Equal(TimeSpan.FromHours(2), options.TeamsCacheTtl);
        Assert.Equal(TimeSpan.FromMilliseconds(500), options.SourcePacingInterval);
    }

    [Fact]
    public void AddBallhog_registers_sane_default_options()
    {
        using ServiceProvider provider = new ServiceCollection().AddBallhogBasketball().BuildServiceProvider();

        BallhogOptions options = provider.GetRequiredService<BallhogOptions>();

        Assert.Null(options.Http.Proxy);
        Assert.Equal(TimeSpan.FromHours(12), options.TeamsCacheTtl);
        Assert.Equal(TimeSpan.FromSeconds(1), options.SourcePacingInterval);
    }
}
