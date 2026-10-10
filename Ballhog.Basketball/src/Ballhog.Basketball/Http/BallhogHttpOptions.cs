namespace Ballhog.Basketball.Http;

/// <summary>
/// Configures the Ballhog HTTP transport pipeline.
/// </summary>
/// <remarks>
/// This is the consumer-facing configuration surface for the transport layer.
/// A consumer whose egress is non-residential can set <see cref="Proxy"/> so
/// outbound NBA requests are routed through a proxy; wiring the proxy into the
/// primary handler is performed by the transport implementation.
/// </remarks>
public sealed class BallhogHttpOptions
{
    /// <summary>
    /// Gets or sets the proxy used for outbound requests, or <see langword="null"/>
    /// to use the platform default (no explicitly configured proxy).
    /// </summary>
    public Uri? Proxy { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of attempts (including the initial attempt)
    /// made for a single request before it is allowed to fail.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets the base delay used for exponential backoff between retry attempts.
    /// </summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Gets or sets the upper bound applied to any single backoff delay between retries.
    /// </summary>
    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromSeconds(10);
}
