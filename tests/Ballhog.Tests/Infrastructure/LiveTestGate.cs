using Xunit;

namespace Ballhog.Tests.Infrastructure;

/// <summary>
/// Gates tests that call the real NBA API (or any other external dependency).
/// A live test is authored as:
/// <code>
/// [Fact]
/// [Trait("Category", "Live")]
/// public void Something_that_talks_to_the_real_api()
/// {
///     LiveTestGate.SkipUnlessEnabled();
///     // ... real network calls ...
/// }
/// </code>
/// When <c>BALLHOG_LIVE</c> is not exactly <c>"1"</c>, the test is reported as
/// SKIPPED (never failed) at run time. Combined with the default run filter
/// <c>Category!=Live</c> (see <c>tests/README.md</c>), live tests are also
/// excluded from every ordinary offline run and CI.
/// </summary>
public static class LiveTestGate
{
    /// <summary>Environment variable that enables live tests.</summary>
    public const string EnvironmentVariableName = "BALLHOG_LIVE";

    /// <summary>Human-readable reason reported when a live test is skipped.</summary>
    public const string SkipReason =
        "Live test skipped because BALLHOG_LIVE is not set to 1. Set BALLHOG_LIVE=1 to run tests that call external services.";

    /// <summary>True when <c>BALLHOG_LIVE=1</c> (ordinal comparison).</summary>
    public static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable(EnvironmentVariableName), "1", StringComparison.Ordinal);

    /// <summary>Skips the running test (xUnit v3 dynamic skip) unless live tests are enabled.</summary>
    public static void SkipUnlessEnabled() => Assert.SkipUnless(IsEnabled, SkipReason);
}