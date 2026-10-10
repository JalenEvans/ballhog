namespace Ballhog.Basketball;

/// <summary>
/// The default <see cref="IBallhogClient"/> implementation.
/// </summary>
/// <remarks>
/// The client is a thin typed facade over an injected <see cref="IStatsProvider"/>. It owns no
/// HTTP transport concerns and never references an <see cref="System.Net.Http.HttpClient"/> or a
/// process-global static client: transport, retries, pacing, and caching all live behind the
/// provider that the container supplies.
/// </remarks>
public sealed class BallhogClient : IBallhogClient
{
    private readonly IStatsProvider _provider;

    /// <summary>
    /// Initializes a new instance of the <see cref="BallhogClient"/> class.
    /// </summary>
    /// <param name="provider">The provider that supplies NBA statistics.</param>
    public BallhogClient(IStatsProvider provider)
    {
        _provider = provider;
    }

    /// <inheritdoc />
    /// <remarks>
    /// This is a pure pass-through to the injected provider. It is declared <see langword="async"/>
    /// only so the awaited call can be configured with <c>ConfigureAwait(false)</c> and the library
    /// never captures the caller's synchronization context; there is no other work in the body.
    /// </remarks>
    public async Task<IReadOnlyList<Team>> GetTeamsAsync(CancellationToken cancellationToken = default)
        => await _provider.GetTeamsAsync(cancellationToken).ConfigureAwait(false);
}
