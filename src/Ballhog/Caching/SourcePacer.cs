namespace Ballhog.Caching;

/// <summary>
/// A shared gate that paces outbound source requests to at most one release per interval.
/// </summary>
/// <remarks>
/// The gate is global across all keys and callers rather than per-key: consecutive
/// <see cref="WaitAsync"/> releases are separated by at least the configured minimum interval.
/// Timing is driven exclusively by the injected <see cref="TimeProvider"/>, never by the system
/// clock or a stopwatch. This type is per-container, not process-global.
/// </remarks>
internal sealed class SourcePacer
{
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _minInterval;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset? _lastRelease;

    /// <summary>
    /// Initializes a new instance of the <see cref="SourcePacer"/> class.
    /// </summary>
    /// <param name="timeProvider">The time source that drives pacing.</param>
    /// <param name="minInterval">The minimum interval enforced between consecutive releases.</param>
    public SourcePacer(TimeProvider timeProvider, TimeSpan minInterval)
    {
        _timeProvider = timeProvider;
        _minInterval = minInterval;
    }

    /// <summary>
    /// Waits until the shared gate permits the next release.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the wait.</param>
    /// <returns>A task that completes when the caller may proceed.</returns>
    public async Task WaitAsync(CancellationToken cancellationToken = default)
    {
        // Serialize globally: while one caller is inside the gate (including any pacing delay),
        // every other caller waits here. This guarantees at least one interval between releases.
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();

            if (_lastRelease is { } lastRelease)
            {
                TimeSpan elapsed = now - lastRelease;
                if (elapsed < _minInterval)
                {
                    TimeSpan remaining = _minInterval - elapsed;
                    await Task.Delay(remaining, _timeProvider, cancellationToken).ConfigureAwait(false);

                    // Re-read the clock: the delay advanced it, and the release is this instant.
                    now = _timeProvider.GetUtcNow();
                }
            }

            // The first caller has no predecessor and releases immediately (initializes the clock).
            _lastRelease = now;
        }
        finally
        {
            _gate.Release();
        }
    }
}
