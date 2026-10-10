using System.Text.Json;
using Ballhog.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ballhog.Tests;

/// <summary>
/// Harness smoke tests for the C-002 test substrate. They prove the offline
/// infrastructure works (fixture loading + DI container) without exercising any
/// library behavior — the library intentionally has no public API yet.
/// </summary>
public sealed class SubstrateSmokeTests
{
    [Fact]
    public void Offline_substrate_loads_a_fixture_and_builds_a_service_provider()
    {
        using var fixture = FixtureLoader.LoadDocument("placeholder");

        Assert.Equal(JsonValueKind.Object, fixture.RootElement.ValueKind);
        Assert.True(
            fixture.RootElement.TryGetProperty("_placeholder", out _),
            "placeholder.json should carry a '_placeholder' marker property so the fixture mechanism is actually exercised.");

        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        Assert.NotNull(provider);
    }
}