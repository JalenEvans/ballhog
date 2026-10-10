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
    /// The minimum per-attempt timeout. Non-positive configured values are clamped to this so the
    /// per-attempt timer always has a positive delay.
    /// </summary>
    private static readonly TimeSpan MinimumRequestTimeout = TimeSpan.FromMilliseconds(1);

    private TimeSpan _requestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the proxy used for outbound requests, or <see langword="null"/>
    /// to use the platform default (no explicitly configured proxy).
    /// </summary>
    public Uri? Proxy { get; set; }

    /// <summary>
    /// Gets or sets the per-attempt timeout applied to a single outbound request attempt
    /// before it is considered failed and eligible for retry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When an attempt does not complete within this interval it is treated as a transient
    /// failure, so it is retried with backoff subject to <see cref="MaxRetryAttempts"/>;
    /// once every attempt has timed out the request surfaces as a
    /// <see cref="BallhogHttpException"/>. The timeout is scheduled through the injected
    /// <see cref="System.TimeProvider"/>, so it stays deterministic in tests.
    /// </para>
    /// <para>
    /// A non-positive value is invalid and is clamped to a one-millisecond minimum. A zero or
    /// negative timeout would otherwise make every attempt time out immediately (or make the
    /// underlying timer throw) rather than disabling the timeout.
    /// </para>
    /// </remarks>
    public TimeSpan RequestTimeout
    {
        get => _requestTimeout;
        set => _requestTimeout = value > TimeSpan.Zero ? value : MinimumRequestTimeout;
    }

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
