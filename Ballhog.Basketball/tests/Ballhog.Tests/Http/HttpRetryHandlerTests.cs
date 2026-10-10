using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Ballhog.Basketball.Http;
using Ballhog.Tests.Infrastructure;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Ballhog.Tests.Http;

/// <summary>
/// Behavior tests for <see cref="HttpRetryHandler"/> (requirement R4, chunk C-004).
/// Retries are bounded by <see cref="BallhogHttpOptions.MaxRetryAttempts"/>, transient
/// failures (5xx and <see cref="HttpRequestException"/>) are retried, HTTP 429 honors
/// <c>Retry-After</c> through the injected <see cref="TimeProvider"/>, and HTTP 403 is
/// treated as terminal (surfaced as <see cref="BallhogHttpException"/>).
/// </summary>
public sealed class HttpRetryHandlerTests
{
    private const string StatsUrl = "https://stats.nba.com/stats/leaguedashteamstats";

    [Fact]
    public async Task Retries_5xx_and_succeeds_on_a_later_attempt()
    {
        var timeProvider = new FakeTimeProvider();
        var options = new BallhogHttpOptions { MaxRetryAttempts = 3 };
        var inner = new ScriptedHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            new HttpResponseMessage(HttpStatusCode.OK));
        var handler = new HttpRetryHandler(timeProvider, options, inner);
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: true);
        using var request = new HttpRequestMessage(HttpMethod.Get, StatsUrl);

        using HttpResponseMessage response = await AwaitWithPumpAsync(
            timeProvider,
            invoker.SendAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task Attempts_are_bounded_by_MaxRetryAttempts()
    {
        var timeProvider = new FakeTimeProvider();
        var options = new BallhogHttpOptions { MaxRetryAttempts = 3 };
        var inner = new ScriptedHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var handler = new HttpRetryHandler(timeProvider, options, inner);
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: true);
        using var request = new HttpRequestMessage(HttpMethod.Get, StatsUrl);

        try
        {
            using HttpResponseMessage response = await AwaitWithPumpAsync(
                timeProvider,
                invoker.SendAsync(request, TestContext.Current.CancellationToken));

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        catch (BallhogHttpException exception)
        {
            Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
        }

        Assert.Equal(3, inner.CallCount);
    }

    [Fact]
    public async Task HttpRequestException_is_retried_and_can_succeed()
    {
        var timeProvider = new FakeTimeProvider();
        var options = new BallhogHttpOptions { MaxRetryAttempts = 3 };
        var inner = new ScriptedHttpMessageHandler((_, call) => call == 1
            ? throw new HttpRequestException("transient failure")
            : new HttpResponseMessage(HttpStatusCode.OK));
        var handler = new HttpRetryHandler(timeProvider, options, inner);
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: true);
        using var request = new HttpRequestMessage(HttpMethod.Get, StatsUrl);

        using HttpResponseMessage response = await AwaitWithPumpAsync(
            timeProvider,
            invoker.SendAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task TooManyRequests_honors_RetryAfter_before_retrying()
    {
        var timeProvider = new FakeTimeProvider();
        var options = new BallhogHttpOptions
        {
            MaxRetryAttempts = 3,
            RetryBaseDelay = TimeSpan.FromMilliseconds(10),
            RetryMaxDelay = TimeSpan.FromMilliseconds(50),
        };

        var tooManyRequests = new HttpResponseMessage((HttpStatusCode)429);
        tooManyRequests.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(2));

        var inner = new ScriptedHttpMessageHandler(
            tooManyRequests,
            new HttpResponseMessage(HttpStatusCode.OK));
        var handler = new HttpRetryHandler(timeProvider, options, inner);
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: true);
        using var request = new HttpRequestMessage(HttpMethod.Get, StatsUrl);

        Task<HttpResponseMessage> send = invoker.SendAsync(request, TestContext.Current.CancellationToken);

        // Retry-After is 2s, so after only 1s the handler must still be waiting.
        Assert.False(send.IsCompleted);
        await AdvanceAsync(timeProvider, TimeSpan.FromSeconds(1));
        Assert.False(send.IsCompleted);

        // Past the Retry-After window the retry fires and the request succeeds.
        await AdvanceAsync(timeProvider, TimeSpan.FromSeconds(2));
        using HttpResponseMessage response = await AwaitWithPumpAsync(timeProvider, send);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task Forbidden_is_not_retried_and_surfaces_as_BallhogHttpException()
    {
        var timeProvider = new FakeTimeProvider();
        var options = new BallhogHttpOptions { MaxRetryAttempts = 3 };
        var inner = new ScriptedHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Forbidden));
        var handler = new HttpRetryHandler(timeProvider, options, inner);
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: true);
        using var request = new HttpRequestMessage(HttpMethod.Get, StatsUrl);

        BallhogHttpException exception = await Assert.ThrowsAsync<BallhogHttpException>(
            () => invoker.SendAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task RequestTimeout_is_retried_and_a_later_attempt_can_succeed()
    {
        var timeProvider = new FakeTimeProvider();
        var options = new BallhogHttpOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(50),
            MaxRetryAttempts = 3,
            RetryBaseDelay = TimeSpan.FromMilliseconds(100),
        };
        // The first attempt hangs forever; the second returns 200 immediately, so only the
        // per-attempt timeout can end attempt one.
        var inner = new HangingScriptedHttpMessageHandler(hangThroughCall: 1);
        var handler = new HttpRetryHandler(timeProvider, options, inner);
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: true);
        using var request = new HttpRequestMessage(HttpMethod.Get, StatsUrl);

        DateTimeOffset start = timeProvider.GetUtcNow();
        Task<HttpResponseMessage> send = invoker.SendAsync(request, TestContext.Current.CancellationToken);

        await SettleAsync();
        Assert.Equal(1, inner.CallCount);
        Assert.False(send.IsCompleted);

        // Past RequestTimeout the timeout leg wins; the 100ms backoff is still pending.
        await AdvanceAndSettleAsync(timeProvider, TimeSpan.FromMilliseconds(50));
        Assert.False(send.IsCompleted);

        // Halfway through the backoff the request must still be pending (no early retry).
        await AdvanceAndSettleAsync(timeProvider, TimeSpan.FromMilliseconds(50));
        Assert.False(send.IsCompleted);

        // Past the backoff the second attempt fires and succeeds immediately.
        await AdvanceAndSettleAsync(timeProvider, TimeSpan.FromMilliseconds(50));
        using HttpResponseMessage response = await send.WaitAsync(
            TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.CallCount);

        // The whole request spent exactly RequestTimeout + backoff, all measured in fake time.
        Assert.Equal(TimeSpan.FromMilliseconds(150), timeProvider.GetUtcNow() - start);
    }

    [Fact]
    public async Task RequestTimeout_exhaustion_surfaces_as_BallhogHttpException_without_a_status_code()
    {
        var timeProvider = new FakeTimeProvider();
        var options = new BallhogHttpOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(50),
            MaxRetryAttempts = 2,
            RetryBaseDelay = TimeSpan.FromMilliseconds(100),
        };
        var inner = new HangingScriptedHttpMessageHandler(hangThroughCall: int.MaxValue);
        var handler = new HttpRetryHandler(timeProvider, options, inner);
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: true);
        using var request = new HttpRequestMessage(HttpMethod.Get, StatsUrl);

        DateTimeOffset start = timeProvider.GetUtcNow();
        Task<HttpResponseMessage> send = invoker.SendAsync(request, TestContext.Current.CancellationToken);

        await SettleAsync();
        Assert.Equal(1, inner.CallCount);
        Assert.False(send.IsCompleted);

        // Attempt 1 times out; the 100ms backoff is pending.
        await AdvanceAndSettleAsync(timeProvider, TimeSpan.FromMilliseconds(50));
        Assert.False(send.IsCompleted);

        // Attempt 2 starts and hangs.
        await AdvanceAndSettleAsync(timeProvider, TimeSpan.FromMilliseconds(100));
        Assert.False(send.IsCompleted);
        Assert.Equal(2, inner.CallCount);

        // Attempt 2 times out: attempts are exhausted, so the failure is surfaced as a
        // BallhogHttpException with no response-derived status code - never a raw
        // TaskCanceledException leak.
        await AdvanceAndSettleAsync(timeProvider, TimeSpan.FromMilliseconds(50));

        BallhogHttpException exception = await Assert.ThrowsAsync<BallhogHttpException>(
            () => send.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        Assert.Null(exception.StatusCode);
        Assert.Equal(2, inner.CallCount);
        Assert.Equal(TimeSpan.FromMilliseconds(200), timeProvider.GetUtcNow() - start);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_unwrapped_and_is_never_retried()
    {
        var timeProvider = new FakeTimeProvider();
        var options = new BallhogHttpOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(50),
            MaxRetryAttempts = 3,
            RetryBaseDelay = TimeSpan.FromMilliseconds(100),
        };
        var inner = new HangingScriptedHttpMessageHandler(hangThroughCall: int.MaxValue);
        var handler = new HttpRetryHandler(timeProvider, options, inner);
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: true);
        using var request = new HttpRequestMessage(HttpMethod.Get, StatsUrl);

        using var callerCts = new CancellationTokenSource();
        callerCts.Cancel();

        // An already-canceled caller token must surface as OperationCanceledException (not
        // BallhogHttpException) and must never start a retry attempt.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => invoker.SendAsync(request, callerCts.Token));

        Assert.Equal(0, inner.CallCount);
    }

    /// <summary>
    /// Completes a retry chain whose delays are scheduled on <see cref="FakeTimeProvider"/>:
    /// pump the fake clock (letting the continuation chain run) until the task settles, then
    /// await it directly. A real-time cap converts a genuine hang into a loud failure; it is
    /// never a success-path budget, so the suite cannot pass on a weakened poll limit.
    /// </summary>
    private static async Task<T> AwaitWithPumpAsync<T>(FakeTimeProvider timeProvider, Task<T> task)
    {
        using var stop = new CancellationTokenSource();
        Task pump = PumpUntilCompleteAsync(timeProvider, task, stop.Token);

        Task winner = await Task.WhenAny(
            pump,
            Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        stop.Cancel();

        Assert.True(ReferenceEquals(winner, pump), "retry chain did not complete within 10s");
        T result = await task; // rethrows if the send faulted
        await pump;
        return result;
    }

    /// <summary>
    /// Advances fake time one backoff window at a time until the observed task completes or
    /// the real-time safety cap cancels the pump.
    /// </summary>
    private static async Task PumpUntilCompleteAsync(
        FakeTimeProvider timeProvider,
        Task task,
        CancellationToken cancellationToken)
    {
        while (!task.IsCompleted && !cancellationToken.IsCancellationRequested)
        {
            timeProvider.Advance(TimeSpan.FromMinutes(1));
            await Task.Yield();
        }
    }

    /// <summary>
    /// Advances fake time by an exact amount and yields once so the scheduled continuation
    /// gets a chance to run before the caller asserts on the task's state. This is a settle
    /// step, not a completion poll: callers only ever assert the task is still pending here.
    /// </summary>
    private static async Task AdvanceAsync(FakeTimeProvider timeProvider, TimeSpan amount)
    {
        timeProvider.Advance(amount);
        await Task.Yield();
    }

    /// <summary>
    /// Yields repeatedly without moving the fake clock so a scheduled continuation chain can
    /// drain. Timers fire inline inside <see cref="FakeTimeProvider.Advance"/>, but each hop
    /// of the await chain is queued to the thread pool, so a generous number of yields lets
    /// the chain run no matter how the scheduler interleaves it. Fake time never moves here,
    /// so exact elapsed assertions elsewhere in the tests stay exact.
    /// </summary>
    private static async Task SettleAsync(int hops = 32)
    {
        for (int i = 0; i < hops; i++)
        {
            await Task.Yield();
        }
    }

    /// <summary>
    /// Advances fake time by an exact amount and then drains the continuation chain without
    /// moving the clock further, so the handler reaches its next scheduled point.
    /// </summary>
    private static async Task AdvanceAndSettleAsync(FakeTimeProvider timeProvider, TimeSpan amount)
    {
        timeProvider.Advance(amount);
        await SettleAsync();
    }

    /// <summary>
    /// A scripted inner handler that HANGS: its <see cref="SendAsync"/> awaits a
    /// <see cref="TaskCompletionSource{TResult}"/> that only completes when the attempt token
    /// is canceled. With the retry handler's linked per-attempt timeout, this makes the
    /// timeout leg win deterministically as soon as fake time advances past
    /// <see cref="BallhogHttpOptions.RequestTimeout"/>. Calls beyond
    /// <paramref name="hangThroughCall"/> return an OK response immediately, so a single
    /// instance can script "timeout then success". An already-canceled token is observed
    /// before the call is counted, mirroring a real transport that refuses to start work.
    /// </summary>
    private sealed class HangingScriptedHttpMessageHandler : HttpMessageHandler
    {
        private readonly int _hangThroughCall;
        private int _callCount;

        public HangingScriptedHttpMessageHandler(int hangThroughCall)
        {
            _hangThroughCall = hangThroughCall;
        }

        /// <summary>The number of attempts that began real work (past the pre-cancel check).</summary>
        public int CallCount => Volatile.Read(ref _callCount);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int call = Interlocked.Increment(ref _callCount);
            if (call > _hangThroughCall)
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(static state => ((TaskCompletionSource<HttpResponseMessage>)state!).TrySetCanceled(), gate))
            {
                return await gate.Task.ConfigureAwait(false);
            }
        }
    }
}
