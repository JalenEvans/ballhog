using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Ballhog.Http;
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

        using HttpResponseMessage response = await DriveAsync(
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
            using HttpResponseMessage response = await DriveAsync(
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

        using HttpResponseMessage response = await DriveAsync(
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
        using HttpResponseMessage response = await WaitForAsync(send);

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

    private static async Task<T> DriveAsync<T>(FakeTimeProvider timeProvider, Task<T> task)
    {
        for (int i = 0; i < 100 && !task.IsCompleted; i++)
        {
            await AdvanceAsync(timeProvider, TimeSpan.FromMinutes(1));
        }

        return await task;
    }

    private static async Task AdvanceAsync(FakeTimeProvider timeProvider, TimeSpan amount)
    {
        timeProvider.Advance(amount);
        await Task.Yield();
    }

    private static async Task<T> WaitForAsync<T>(Task<T> task)
    {
        for (int i = 0; i < 1000 && !task.IsCompleted; i++)
        {
            await Task.Yield();
        }

        return await task;
    }
}
