namespace Ballhog.Basketball;

/// <summary>
/// The typed, asynchronous public entry point for querying NBA statistics.
/// </summary>
/// <remarks>
/// This is the consumer-facing client surface. It is deliberately expressed as an interface so
/// consumers can depend on the abstraction and swap implementations (for example a fake in tests)
/// without touching transport or caching details. The surface grows slice by slice; the members
/// exposed today cover the league-wide team set, and further query methods are added in later
/// slices.
/// </remarks>
public interface IBallhogClient
{
    /// <summary>
    /// Retrieves the set of NBA teams.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the asynchronous operation.</param>
    /// <returns>A read-only list of the available <see cref="Team"/> instances.</returns>
    Task<IReadOnlyList<Team>> GetTeamsAsync(CancellationToken cancellationToken = default);
}
