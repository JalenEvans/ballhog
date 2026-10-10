using System.Collections.Concurrent;

namespace Ballhog.Caching;

/// <summary>
/// A thread-safe, in-process cache-aside store with single-flight de-duplication.
/// </summary>
/// <remarks>
/// Entries are keyed by a caller-supplied string (see <see cref="CacheKeys"/>) and carry a
/// time-to-live. Concurrent misses for the same key are collapsed onto a single underlying
/// factory execution whose task is shared by every waiter. Successful results are stored until
/// their TTL elapses; failures are never cached, so the next call re-invokes the factory. Expiry
/// and timekeeping are driven exclusively by the injected <see cref="TimeProvider"/>, never by
/// the system clock. This type is per-container, not process-global.
/// </remarks>
internal sealed class BallhogCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _defaultTtl;

    /// <summary>
    /// Initializes a new instance of the <see cref="BallhogCache"/> class.
    /// </summary>
    /// <param name="timeProvider">The time source that drives TTL expiry.</param>
    /// <param name="defaultTtl">The time-to-live applied when a caller supplies no override.</param>
    public BallhogCache(TimeProvider timeProvider, TimeSpan defaultTtl)
    {
        _timeProvider = timeProvider;
        _defaultTtl = defaultTtl;
    }

    /// <summary>
    /// Returns the cached value for <paramref name="key"/>, or produces and caches it by
    /// invoking <paramref name="factory"/> exactly once across concurrent misses.
    /// </summary>
    /// <typeparam name="T">The cached value type.</typeparam>
    /// <param name="key">The cache key, built via <see cref="CacheKeys"/>.</param>
    /// <param name="factory">The delegate that produces the value on a miss.</param>
    /// <param name="ttl">An optional per-entry TTL; when omitted <c>defaultTtl</c> is used.</param>
    /// <param name="cancellationToken">A token used to cancel the asynchronous operation.</param>
    /// <returns>The cached or newly produced value.</returns>
    public async Task<T> GetOrAddAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        TimeSpan effectiveTtl = ttl ?? _defaultTtl;

        // The loop only repeats when a racing caller wins the install and we must re-evaluate
        // the entry it published (which may itself have since expired or failed).
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_entries.TryGetValue(key, out CacheEntry? existing))
            {
                if (!existing.IsExpired(_timeProvider.GetUtcNow()))
                {
                    return (T)(await existing.Task.ConfigureAwait(false))!;
                }

                // Expired: evict so a fresh factory execution replaces it.
                _entries.TryRemove(new KeyValuePair<string, CacheEntry>(key, existing));
            }

            Func<CancellationToken, Task<object?>> boxedFactory = async token =>
            {
                T value = await factory(token).ConfigureAwait(false);
                return value;
            };

            var candidate = new CacheEntry(_timeProvider, effectiveTtl, boxedFactory, cancellationToken);

            CacheEntry entry = _entries.GetOrAdd(key, candidate);
            if (!ReferenceEquals(entry, candidate))
            {
                // Another caller published first; re-evaluate their entry instead of ours.
                continue;
            }

            try
            {
                return (T)(await entry.Task.ConfigureAwait(false))!;
            }
            catch
            {
                // D-2: never cache a failure. Drop only our own entry so the next call retries.
                _entries.TryRemove(new KeyValuePair<string, CacheEntry>(key, entry));
                throw;
            }
        }
    }

    /// <summary>
    /// A single cache slot: one lazily-started factory task shared by every waiter, plus the
    /// timestamp/TTL that gate expiry once the task completes successfully.
    /// </summary>
    private sealed class CacheEntry
    {
        private readonly Lazy<Task<object?>> _task;
        private readonly TimeProvider _timeProvider;
        private readonly TimeSpan _ttl;
        private DateTimeOffset _cachedAt;

        public CacheEntry(
            TimeProvider timeProvider,
            TimeSpan ttl,
            Func<CancellationToken, Task<object?>> factory,
            CancellationToken cancellationToken)
        {
            _timeProvider = timeProvider;
            _ttl = ttl;

            // ExecutionAndPublication guarantees a single factory run across all waiters.
            _task = new Lazy<Task<object?>>(
                () => RunAsync(factory, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <summary>The shared factory task; first access starts the single execution.</summary>
        public Task<object?> Task => _task.Value;

        /// <summary>
        /// Reports whether this slot should be treated as a miss. A not-yet-started or still
        /// in-flight task is never expired; a faulted/canceled task is a miss so it can be
        /// replaced; a completed success expires once its TTL has elapsed.
        /// </summary>
        public bool IsExpired(DateTimeOffset now)
        {
            if (!_task.IsValueCreated)
            {
                return false;
            }

            Task<object?> task = _task.Value;
            if (!task.IsCompleted)
            {
                return false;
            }

            if (task.IsFaulted || task.IsCanceled)
            {
                return true;
            }

            return now - _cachedAt >= _ttl;
        }

        private async Task<object?> RunAsync(
            Func<CancellationToken, Task<object?>> factory,
            CancellationToken cancellationToken)
        {
            object? value = await factory(cancellationToken).ConfigureAwait(false);

            // The TTL clock starts at the moment the value is actually cached (task completion),
            // not when the factory was invoked.
            _cachedAt = _timeProvider.GetUtcNow();
            return value;
        }
    }
}