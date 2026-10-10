using System.Globalization;

namespace Ballhog.Basketball.Caching;

/// <summary>
/// Builds the deterministic, collision-free cache keys used by <see cref="BallhogCache"/>.
/// </summary>
/// <remarks>
/// Every key carries the version prefix so that a future schema change can invalidate all
/// previously cached entries at once. Keys are namespaced by data family and then by their
/// discriminating arguments, covering the teams, players, season-stats, games, and game-log
/// variants.
/// </remarks>
internal static class CacheKeys
{
    /// <summary>
    /// The schema version prefix that begins every cache key.
    /// </summary>
    public static string VersionPrefix { get; } = "ballhog:v1:";

    /// <summary>
    /// The key for the league-wide team set.
    /// </summary>
    public static string Teams { get; } = "ballhog:v1:teams";

    /// <summary>
    /// Builds the key for the players of a given season.
    /// </summary>
    /// <param name="season">The season identifier (for example <c>2024-25</c>).</param>
    /// <returns>The players cache key.</returns>
    public static string Players(string season)
        => $"{VersionPrefix}players:{season}";

    /// <summary>
    /// Builds the key for a season/measure statistic scoped to a single entity.
    /// </summary>
    /// <param name="season">The season identifier (for example <c>2024-25</c>).</param>
    /// <param name="measureType">The measure type (for example <c>Base</c>).</param>
    /// <param name="entityId">The player or team identifier.</param>
    /// <returns>The season-stats cache key.</returns>
    public static string SeasonStats(string season, string measureType, int entityId)
        => $"{VersionPrefix}season-stats:{season}:{measureType}:{entityId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Builds the key for the games played on a given date.
    /// </summary>
    /// <param name="date">The game date, in the source's canonical format.</param>
    /// <returns>The games cache key.</returns>
    public static string Games(string date)
        => $"{VersionPrefix}games:{date}";

    /// <summary>
    /// Builds the key for the game log of a season scoped to a single entity.
    /// </summary>
    /// <param name="season">The season identifier (for example <c>2024-25</c>).</param>
    /// <param name="entityId">The player or team identifier.</param>
    /// <returns>The game-logs cache key.</returns>
    public static string GameLogs(string season, int entityId)
        => $"{VersionPrefix}game-logs:{season}:{entityId.ToString(CultureInfo.InvariantCulture)}";
}
