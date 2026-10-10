using Ballhog.Caching;
using Xunit;

namespace Ballhog.Tests.Caching;

/// <summary>
/// Behavior tests for <see cref="CacheKeys"/> (requirement R5, Jira BALLHOG-12, chunk C-006).
/// Every generated key is deterministic and carries the version prefix, and the distinct
/// data families never collide.
/// </summary>
public sealed class CacheKeysTests
{
    [Fact]
    public void CacheKeys_use_the_versioned_schema()
    {
        Assert.Equal("ballhog:v1:", CacheKeys.VersionPrefix);
        Assert.Equal("ballhog:v1:teams", CacheKeys.Teams);

        // Generated keys must also be scoped by the same version prefix.
        Assert.StartsWith(CacheKeys.VersionPrefix, CacheKeys.Players("2025-26"), StringComparison.Ordinal);
        Assert.StartsWith(CacheKeys.VersionPrefix, CacheKeys.SeasonStats("2025-26", "Base", 1), StringComparison.Ordinal);
        Assert.StartsWith(CacheKeys.VersionPrefix, CacheKeys.Games("2026-01-01"), StringComparison.Ordinal);
        Assert.StartsWith(CacheKeys.VersionPrefix, CacheKeys.GameLogs("2025-26", 1), StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_keys_are_deterministic_for_the_same_inputs()
    {
        Assert.Equal(CacheKeys.Players("2025-26"), CacheKeys.Players("2025-26"));
        Assert.Equal(CacheKeys.SeasonStats("2025-26", "Base", 1), CacheKeys.SeasonStats("2025-26", "Base", 1));
        Assert.Equal(CacheKeys.Games("2026-01-01"), CacheKeys.Games("2026-01-01"));
        Assert.Equal(CacheKeys.GameLogs("2025-26", 1), CacheKeys.GameLogs("2025-26", 1));
    }

    [Fact]
    public void Keys_are_pairwise_distinct()
    {
        string[] keys =
        [
            CacheKeys.Teams,
            CacheKeys.Players("2025-26"),
            CacheKeys.Players("2024-25"),
            CacheKeys.SeasonStats("2025-26", "Base", 1),
            CacheKeys.SeasonStats("2025-26", "Base", 2),
            CacheKeys.Games("2026-01-01"),
            CacheKeys.GameLogs("2025-26", 1),
        ];

        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
    }
}
