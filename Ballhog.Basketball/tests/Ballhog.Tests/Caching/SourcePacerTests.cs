using Ballhog.Basketball.Caching;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Ballhog.Tests.Caching;

/// <summary>
/// Behavior tests for <see cref="SourcePacer"/> (requirement R5, Jira BALLHOG-12, chunk C-006).
/// The pacer is a single global gate: consecutive releases are separated by at least the
/// configured minimum interval, and concurrent callers are serialized one interval apart.
/// Timing is driven exclusively by <see cref="FakeTimeProvider"/>.
/// </summary>
public sealed class SourcePacerTests
{
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task WaitAsync_spaces_consecutive_releases_by_at_least_the_minimum_interval()
    {
        var time = new FakeTimeProvider();
        var pacer = new SourcePacer(time, MinInterval);

        await pacer.WaitAsync(TestContext.Current.CancellationToken);
        DateTimeOffset firstRelease = time.GetUtcNow();

        Task second = pacer.WaitAsync(TestContext.Current.CancellationToken);

        // One tick short of the interval: the second release must still be waiting.
        time.Advance(MinInterval - TimeSpan.FromMilliseconds(1));
        await Task.Yield();
        Assert.False(second.IsCompleted);

        time.Advance(TimeSpan.FromMilliseconds(2));
        await second.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        DateTimeOffset secondRelease = time.GetUtcNow();

        Assert.True(
            secondRelease - firstRelease >= MinInterval,
            $"Expected the second release at least {MinInterval} after the first, but it was {secondRelease - firstRelease} later.");
    }

    [Fact]
    public async Task WaitAsync_serializes_concurrent_callers_globally()
    {
        var time = new FakeTimeProvider();
        var pacer = new SourcePacer(time, MinInterval);

        // Consume the immediate first release so the two queued callers both have to wait.
        await pacer.WaitAsync(TestContext.Current.CancellationToken);

        Task a = pacer.WaitAsync(TestContext.Current.CancellationToken);
        Task b = pacer.WaitAsync(TestContext.Current.CancellationToken);

        Assert.False(a.IsCompleted);
        Assert.False(b.IsCompleted);

        // A single interval may release exactly one caller; await that release directly
        // (thread-pool hops included) instead of polling. The other must wait another interval.
        time.Advance(MinInterval);
        await Task.WhenAny(a, b).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.True(a.IsCompleted ^ b.IsCompleted, "Exactly one caller must be released per interval.");

        Task remaining = a.IsCompleted ? b : a;
        await WaitForReleaseAsync(time, remaining);

        Assert.True(a.IsCompleted);
        Assert.True(b.IsCompleted);
    }

    private static async Task WaitForReleaseAsync(FakeTimeProvider time, Task task)
    {
        // A released caller only registers the next caller's pacer delay once its own
        // continuation has run, so a pump keeps advancing fake time. The caller itself is
        // awaited directly (following the full continuation chain); the real-time cap only
        // converts a genuine hang into a loud failure, never a success-path budget.
        using var stop = new CancellationTokenSource();
        Task pump = AdvanceUntilCompleteAsync(time, task, stop.Token);

        Task winner = await Task.WhenAny(
            pump,
            Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        stop.Cancel();

        Assert.True(ReferenceEquals(winner, pump), "pacer caller was not released within 10s");
        await task; // rethrows if the caller faulted
        await pump;
    }

    private static async Task AdvanceUntilCompleteAsync(
        FakeTimeProvider time,
        Task task,
        CancellationToken cancellationToken)
    {
        while (!task.IsCompleted && !cancellationToken.IsCancellationRequested)
        {
            time.Advance(MinInterval);
            await Task.Yield();
        }
    }
}
