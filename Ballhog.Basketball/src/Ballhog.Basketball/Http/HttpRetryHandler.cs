using System.Net;
using System.Net.Http.Headers;

namespace Ballhog.Basketball.Http;

/// <summary>
/// A <see cref="DelegatingHandler"/> that retries transient NBA request failures using
/// bounded exponential backoff and honors server-requested pacing.
/// </summary>
/// <remarks>
/// <para>
/// Transient failures (HTTP 5xx, request timeouts, and <see cref="HttpRequestException"/>)
/// are retried with backoff. Each attempt is bounded by
/// <see cref="BallhogHttpOptions.RequestTimeout"/>; a per-attempt timeout is retried like any
/// other transient failure and, once <see cref="BallhogHttpOptions.MaxRetryAttempts"/> is
/// exhausted, surfaces as a <see cref="BallhogHttpException"/>. An HTTP 429 response is
/// retried honoring the <c>Retry-After</c> header when present, falling back to backoff
/// otherwise. An HTTP 403 response is treated as blocked and is never retried, per approved
/// decision D-1.
/// </para>
/// <para>
/// Caller cancellation is propagated unwrapped as an <see cref="OperationCanceledException"/>
/// and is never retried or folded into a timeout. The per-attempt timeout is scheduled through
/// the injected <see cref="TimeProvider"/> rather than a real timer, so the timeout/retry race
/// stays deterministic under a fake clock in tests.
/// </para>
/// <para>
/// Attempts are bounded by <see cref="BallhogHttpOptions.MaxRetryAttempts"/>, so the handler
/// cannot storm the source. All delays are scheduled through the injected
/// <see cref="TimeProvider"/> rather than wall-clock time, keeping retry timing deterministic
/// in tests.
/// </para>
/// <para>
/// Any response that is not a success and is not retried to exhaustion is surfaced as a
/// <see cref="BallhogHttpException"/> carrying its <see cref="HttpStatusCode"/>; this makes
/// the terminal outcome consistent for HTTP 403 and for non-retryable 4xx codes such as
/// 400 and 404, rather than returning a response the rest of the pipeline would have to
/// keep re-checking.
/// </para>
/// </remarks>
internal sealed class HttpRetryHandler : DelegatingHandler
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HttpRetryHandler"/> class with default options.
    /// </summary>
    /// <param name="timeProvider">The time provider used to schedule retry delays.</param>
    public HttpRetryHandler(TimeProvider timeProvider)
        : this(timeProvider, new BallhogHttpOptions())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpRetryHandler"/> class with the supplied options.
    /// </summary>
    /// <param name="timeProvider">The time provider used to schedule retry delays.</param>
    /// <param name="options">The retry configuration to apply.</param>
    public HttpRetryHandler(TimeProvider timeProvider, BallhogHttpOptions options)
    {
        TimeProvider = timeProvider;
        Options = options;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpRetryHandler"/> class chained to
    /// the supplied inner handler.
    /// </summary>
    /// <param name="timeProvider">The time provider used to schedule retry delays.</param>
    /// <param name="options">The retry configuration to apply.</param>
    /// <param name="innerHandler">The handler that processes each retry attempt.</param>
    public HttpRetryHandler(TimeProvider timeProvider, BallhogHttpOptions options, HttpMessageHandler innerHandler)
        : this(timeProvider, options)
    {
        InnerHandler = innerHandler;
    }

    /// <summary>
    /// Gets the time provider used to schedule retry delays.
    /// </summary>
    public TimeProvider TimeProvider { get; }

    /// <summary>
    /// Gets the retry configuration applied by this handler.
    /// </summary>
    public BallhogHttpOptions Options { get; }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        int maxAttempts = Math.Max(1, Options.MaxRetryAttempts);

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            HttpResponseMessage response;

            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                // Race the send against a per-attempt timeout scheduled on the injected
                // TimeProvider. Task.Delay with a provider keeps the timeout deterministic
                // under a fake clock; the linked CTS lets the timeout cancel the in-flight
                // send while still letting a caller cancellation cancel both legs.
                Task<HttpResponseMessage> sendTask = base.SendAsync(request, linked.Token);
                Task timeoutTask = Task.Delay(Options.RequestTimeout, TimeProvider, linked.Token);

                Task completed = await Task.WhenAny(sendTask, timeoutTask).ConfigureAwait(false);

                // Stop the timeout timer as soon as either leg settles. Cancelling the linked
                // source also cancels the losing leg so it can never outlive the attempt.
                linked.Cancel();

                if (!ReferenceEquals(completed, sendTask))
                {
                    // The timeout leg won. A cancelled caller token means the caller
                    // cancelled rather than the attempt timing out, so propagate that
                    // unwrapped instead of treating it as a retryable failure.
                    cancellationToken.ThrowIfCancellationRequested();

                    DiscardAbandonedAttempt(sendTask);

                    if (attempt >= maxAttempts)
                    {
                        throw new BallhogHttpException(
                            "The NBA request timed out after exhausting all retry attempts.",
                            statusCode: null);
                    }

                    await DelayAsync(GetExponentialBackoff(attempt), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    response = await sendTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Caller cancellation propagates unwrapped and is never retried.
                    throw;
                }
                catch (OperationCanceledException exception)
                {
                    // A cancellation from the linked (timeout) token is a per-attempt timeout.
                    // The race above normally catches it first, but the send can observe the
                    // cancel concurrently, so treat it as transient here too.
                    if (attempt >= maxAttempts)
                    {
                        throw new BallhogHttpException(
                            "The NBA request timed out after exhausting all retry attempts.",
                            statusCode: null,
                            exception);
                    }

                    await DelayAsync(GetExponentialBackoff(attempt), cancellationToken).ConfigureAwait(false);
                    continue;
                }
                catch (HttpRequestException exception)
                {
                    if (attempt >= maxAttempts)
                    {
                        throw new BallhogHttpException(
                            "The NBA request failed with a transport error after exhausting all retry attempts.",
                            statusCode: null,
                            exception);
                    }

                    await DelayAsync(GetExponentialBackoff(attempt), cancellationToken).ConfigureAwait(false);
                    continue;
                }
            }

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            using (response)
            {
                HttpStatusCode status = response.StatusCode;

                bool isRetryable = (int)status >= 500 || status == (HttpStatusCode)429;

                if (!isRetryable || attempt >= maxAttempts)
                {
                    throw new BallhogHttpException(
                        $"The NBA request failed with HTTP {(int)status} ({status}).",
                        status);
                }

                TimeSpan delay = status == (HttpStatusCode)429 && response.Headers.RetryAfter is { } retryAfter
                    ? ResolveRetryAfter(retryAfter)
                    : GetExponentialBackoff(attempt);

                await DelayAsync(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        // The loop always returns or throws; this guards the compiler's control-flow analysis.
        throw new BallhogHttpException(
            "The NBA request failed without producing a response.",
            statusCode: null);
    }

    /// <summary>
    /// Observes the leg abandoned when a per-attempt timeout wins the race, so a late fault is
    /// never unobserved and a late success never leaks a response.
    /// </summary>
    /// <param name="sendTask">The attempt task that lost the timeout race.</param>
    private static void DiscardAbandonedAttempt(Task<HttpResponseMessage> sendTask)
    {
        _ = sendTask.ContinueWith(
            static task =>
            {
                if (task.Status == TaskStatus.RanToCompletion)
                {
                    task.Result.Dispose();
                }
                else
                {
                    _ = task.Exception;
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Computes the deterministic exponential backoff for the attempt that just failed.
    /// </summary>
    /// <param name="attempt">The 1-based number of the failed attempt.</param>
    /// <returns>The delay to wait before the next attempt, capped at <see cref="BallhogHttpOptions.RetryMaxDelay"/>.</returns>
    private TimeSpan GetExponentialBackoff(int attempt)
    {
        double baseMilliseconds = Options.RetryBaseDelay.TotalMilliseconds;
        double maxMilliseconds = Options.RetryMaxDelay.TotalMilliseconds;

        double milliseconds = baseMilliseconds * Math.Pow(2, attempt - 1);
        if (maxMilliseconds >= 0 && milliseconds > maxMilliseconds)
        {
            milliseconds = maxMilliseconds;
        }

        return milliseconds > 0 ? TimeSpan.FromMilliseconds(milliseconds) : TimeSpan.Zero;
    }

    /// <summary>
    /// Resolves the delay requested by an HTTP 429 <c>Retry-After</c> header.
    /// </summary>
    /// <param name="retryAfter">The parsed <c>Retry-After</c> header value.</param>
    /// <returns>
    /// The requested delay in seconds or as a date relative to <see cref="TimeProvider"/>;
    /// <see cref="TimeSpan.Zero"/> when the header carries no usable future time.
    /// </returns>
    private TimeSpan ResolveRetryAfter(RetryConditionHeaderValue retryAfter)
    {
        if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter.Date is { } date)
        {
            TimeSpan wait = date - TimeProvider.GetUtcNow();
            if (wait > TimeSpan.Zero)
            {
                return wait;
            }
        }

        return TimeSpan.Zero;
    }

    /// <summary>
    /// Waits for the supplied delay using the injected <see cref="TimeProvider"/>.
    /// </summary>
    /// <param name="delay">The delay to wait; non-positive delays complete immediately.</param>
    /// <param name="cancellationToken">The token that cancels the wait.</param>
    /// <returns>A task that completes when the delay has elapsed.</returns>
    private Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        => delay > TimeSpan.Zero
            ? Task.Delay(delay, TimeProvider, cancellationToken)
            : Task.CompletedTask;
}
