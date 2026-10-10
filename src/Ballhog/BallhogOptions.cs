using Ballhog.Http;

namespace Ballhog;

/// <summary>
/// Configures the Ballhog client graph registered by
/// <c>Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.AddBallhog</c>.
/// </summary>
/// <remarks>
/// This aggregates the consumer-facing knobs for every layer of the stack. Transport settings are
/// composed through <see cref="Http"/> rather than duplicated, so retry and proxy behavior stay
/// defined in exactly one place.
/// </remarks>
public sealed class BallhogOptions
{
    /// <summary>
    /// Gets the transport pipeline options applied to every outbound NBA request.
    /// </summary>
    /// <remarks>
    /// The instance is created with the options object and mutated in place by the caller, so
    /// consumers configure retry and proxy behavior here rather than on a separate type.
    /// </remarks>
    public BallhogHttpOptions Http { get; } = new();

    /// <summary>
    /// Gets or sets the minimum interval enforced between consecutive outbound source requests.
    /// </summary>
    /// <remarks>
    /// Pacing is global across the client graph: successive source calls are separated by at least
    /// this interval. It applies only on a cache miss, so cache hits incur no pacing delay.
    /// </remarks>
    public TimeSpan SourcePacingInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the time-to-live for the cached league-wide team set.
    /// </summary>
    /// <remarks>
    /// A cached team set is served without source I/O until this interval elapses; the next call
    /// after expiry re-fetches it.
    /// </remarks>
    public TimeSpan TeamsCacheTtl { get; set; } = TimeSpan.FromHours(12);
}
