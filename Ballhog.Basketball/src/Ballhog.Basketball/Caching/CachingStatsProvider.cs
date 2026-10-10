namespace Ballhog.Basketball.Caching;

/// <summary>
/// An <see cref="IStatsProvider"/> decorator that adds cache-aside storage and source pacing
/// around an inner provider.
/// </summary>
/// <remarks>
/// Reads are served from the injected <see cref="BallhogCache"/> when fresh and otherwise
/// delegated to the inner provider behind the shared <see cref="SourcePacer"/>. All of its
/// collaborators are supplied by the container, so the decorator holds no static or
/// process-global state.
/// </remarks>
internal sealed class CachingStatsProvider : IStatsProvider
{
    private readonly IStatsProvider _inner;
    private readonly BallhogCache _cache;
    private readonly SourcePacer _pacer;
    private readonly TimeSpan _teamsTtl;

    /// <summary>
    /// Initializes a new instance of the <see cref="CachingStatsProvider"/> class.
    /// </summary>
    /// <param name="inner">The provider whose results are cached.</param>
    /// <param name="cache">The cache-aside store.</param>
    /// <param name="pacer">The shared source pacing gate.</param>
    /// <param name="teamsTtl">The time-to-live for the league-wide team set.</param>
    public CachingStatsProvider(IStatsProvider inner, BallhogCache cache, SourcePacer pacer, TimeSpan teamsTtl)
    {
        _inner = inner;
        _cache = cache;
        _pacer = pacer;
        _teamsTtl = teamsTtl;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Team>> GetTeamsAsync(CancellationToken cancellationToken = default)
        => _cache.GetOrAddAsync(
            CacheKeys.Teams,
            async token =>
            {
                // Pacing lives inside the factory: it applies only on a cache miss, so a hit
                // performs no source I/O and no pacing delay. Under single-flight only the
                // lead caller runs this factory, yielding one pacer wait and one inner call.
                await _pacer.WaitAsync(token).ConfigureAwait(false);
                return await _inner.GetTeamsAsync(token).ConfigureAwait(false);
            },
            _teamsTtl,
            cancellationToken);
}
