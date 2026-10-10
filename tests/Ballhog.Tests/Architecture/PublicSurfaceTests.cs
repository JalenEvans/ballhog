using System.Reflection;
using Xunit;

namespace Ballhog.Tests.Architecture;

/// <summary>
/// Architecture guard for the public API surface (requirement R7, Jira BALLHOG-14, chunk
/// C-008; test T-010). The library's consumer contract is exactly the approved allowlist:
/// provider, caching, pacing, delegating-handler, and wire-DTO types are implementation
/// detail and must never leak into the public surface.
/// </summary>
/// <remarks>
/// The Public API analyzer baseline (<c>PublicAPI.Shipped.txt</c> +
/// <c>PublicAPI.Unshipped.txt</c>) already locks the compiled surface at build time. This
/// test does not duplicate that mechanism; it documents and defends the same human-intent
/// rule ("no provider-specific types in the public API") at run time on both target
/// frameworks.
/// </remarks>
public sealed class PublicSurfaceTests
{
    /// <summary>The complete, intended set of public types in the Ballhog library.</summary>
    private static readonly string[] ApprovedPublicTypes =
    [
        "Ballhog.Team",
        "Ballhog.IStatsProvider",
        "Ballhog.IBallhogClient",
        "Ballhog.BallhogClient",
        "Ballhog.BallhogOptions",
        "Ballhog.Http.BallhogHttpOptions",
        "Ballhog.Http.BallhogHttpException",
        "Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions",
    ];

    /// <summary>
    /// Implementation types that must stay internal: the Stats NBA provider, the cache-aside
    /// decorator and pacer, the delegating handlers, the source-generated JSON serializer
    /// context, and the wire DTOs. Listed explicitly so the "nothing provider-specific
    /// leaks" rule fails loudly and names the offender.
    /// </summary>
    private static readonly string[] MustRemainInternal =
    [
        "Ballhog.Providers.StatsNbaProvider",
        "Ballhog.Providers.TeamYearsResponse",
        "Ballhog.Providers.TeamYearsResultSets",
        "Ballhog.Providers.BallhogJsonContext",
        "Ballhog.Caching.CachingStatsProvider",
        "Ballhog.Caching.BallhogCache",
        "Ballhog.Caching.SourcePacer",
        "Ballhog.Caching.CacheKeys",
        "Ballhog.Http.HttpRetryHandler",
        "Ballhog.Http.NbaRequestHeadersHandler",
        "Ballhog.Http.NbaQueryStringBuilder",
    ];

    [Fact]
    public void Only_the_approved_types_are_public()
    {
        string[] actual = PublicTypeNames();

        string[] unexpected = actual.Except(ApprovedPublicTypes, StringComparer.Ordinal).ToArray();
        string[] missing = ApprovedPublicTypes.Except(actual, StringComparer.Ordinal).ToArray();

        Assert.True(
            unexpected.Length == 0,
            "The library exposes types outside the approved public surface: " + string.Join(", ", unexpected));
        Assert.True(
            missing.Length == 0,
            "Approved public types are missing from the surface: " + string.Join(", ", missing));
    }

    [Fact]
    public void No_provider_implementation_type_leaks_into_the_public_surface()
    {
        Assembly assembly = typeof(Team).Assembly;
        HashSet<string> allTypes = assembly.GetTypes()
            .Select(type => type.FullName!)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> publicTypes = PublicTypeNames().ToHashSet(StringComparer.Ordinal);

        // The guard is only meaningful while the types it names still exist...
        string[] vanished = MustRemainInternal.Where(name => !allTypes.Contains(name)).ToArray();
        Assert.True(
            vanished.Length == 0,
            "This guard names implementation types that no longer exist; update the list rather than deleting the rule: "
                + string.Join(", ", vanished));

        // ...and none of them may be visible to consumers.
        string[] leaked = MustRemainInternal.Where(publicTypes.Contains).ToArray();
        Assert.True(
            leaked.Length == 0,
            "Provider/implementation types must stay internal but are public: " + string.Join(", ", leaked));
    }

    private static string[] PublicTypeNames() =>
        typeof(Team).Assembly
            .GetExportedTypes()
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
}
