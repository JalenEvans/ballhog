namespace Ballhog;

/// <summary>
/// Defines the seam through which Ballhog retrieves NBA statistics from a backing provider.
/// </summary>
/// <remarks>
/// Implemented by the StatsNBA-backed provider today and by future providers (for example
/// Balldontlie). No provider-specific type appears in this contract: callers depend only on the
/// public models Ballhog exposes, so the underlying source can change without affecting consumers.
/// </remarks>
public interface IStatsProvider
{
    /// <summary>
    /// Retrieves the set of NBA teams known to the provider.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the asynchronous operation.</param>
    /// <returns>A read-only list of the available <see cref="Team"/> instances.</returns>
    Task<IReadOnlyList<Team>> GetTeamsAsync(CancellationToken cancellationToken = default);
}
