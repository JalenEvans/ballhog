using Ballhog.Basketball;

namespace Ballhog.Tests.Infrastructure;

/// <summary>
/// A deterministic, offline <see cref="IStatsProvider"/> test double. It records how many
/// times the source was hit and delegates the response to a scripted responder, so tests can
/// assert exactly-once source behavior, simulate failures, and gate calls behind a barrier.
/// </summary>
/// <remarks>
/// A synchronous responder throw is converted into a faulted task (mirroring
/// <see cref="ScriptedHttpMessageHandler"/>) so failure tests observe the exception on
/// <c>await</c> rather than at call time.
/// </remarks>
internal sealed class FakeStatsProvider : IStatsProvider
{
    private readonly Func<int, CancellationToken, Task<IReadOnlyList<Team>>> _responder;
    private int _callCount;

    /// <summary>Creates a fake that always returns <paramref name="teams"/>.</summary>
    public FakeStatsProvider(IReadOnlyList<Team> teams)
        : this((_, _) => Task.FromResult(teams))
    {
    }

    /// <summary>
    /// Creates a fake that responds per call. The first argument is the 1-based call number,
    /// so a responder can throw on the first call and succeed on the second.
    /// </summary>
    public FakeStatsProvider(Func<int, CancellationToken, Task<IReadOnlyList<Team>>> responder)
    {
        _responder = responder ?? throw new ArgumentNullException(nameof(responder));
    }

    /// <summary>The number of times <see cref="GetTeamsAsync"/> has been invoked.</summary>
    public int CallCount => Volatile.Read(ref _callCount);

    /// <inheritdoc />
    public Task<IReadOnlyList<Team>> GetTeamsAsync(CancellationToken cancellationToken = default)
    {
        int call = Interlocked.Increment(ref _callCount);
        try
        {
            return _responder(call, cancellationToken);
        }
        catch (Exception exception)
        {
            return Task.FromException<IReadOnlyList<Team>>(exception);
        }
    }
}
