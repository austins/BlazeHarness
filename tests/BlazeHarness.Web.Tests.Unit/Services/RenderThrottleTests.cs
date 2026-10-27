using BlazeHarness.Web.Services;
using Microsoft.Extensions.Time.Testing;

namespace BlazeHarness.Web.Tests.Unit.Services;

public sealed class RenderThrottleTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(50);

    private readonly FakeTimeProvider _time = new();

    [Fact]
    public async Task Request_ManyCallsInOneInterval_RendersOnce()
    {
        // Arrange
        var renders = 0;
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sut = new RenderThrottle(
            () =>
            {
                Interlocked.Increment(ref renders);
                rendered.TrySetResult();
                return Task.CompletedTask;
            },
            Interval,
            _time);

        // Act
        for (var i = 0; i < 100; i++)
        {
            sut.Request();
        }

        _time.Advance(Interval);
        await rendered.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        _time.Advance(Interval * 4);

        // Assert
        renders.Should().Be(1);
    }

    [Fact]
    public async Task Request_BeforeTheIntervalPasses_DoesNotRender()
    {
        // Arrange
        var renders = 0;
        using var sut = new RenderThrottle(
            () =>
            {
                Interlocked.Increment(ref renders);
                return Task.CompletedTask;
            },
            Interval,
            _time);

        // Act
        sut.Request();
        _time.Advance(Interval - TimeSpan.FromMilliseconds(1));
        await Task.Yield();

        // Assert
        renders.Should().Be(0);
    }

    [Fact]
    public async Task Request_AfterARender_SchedulesAnotherRender()
    {
        // Arrange
        var renders = 0;
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sut = new RenderThrottle(
            () =>
            {
                (Interlocked.Increment(ref renders) == 1 ? first : second).TrySetResult();
                return Task.CompletedTask;
            },
            Interval,
            _time);

        // Act
        sut.Request();
        _time.Advance(Interval);
        await first.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        sut.Request();
        _time.Advance(Interval);

        // Assert
        await second.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        renders.Should().Be(2);
    }

    [Fact]
    public async Task Dispose_WhileWaiting_SkipsTheRender()
    {
        // Arrange
        var renders = 0;

        // Act
        using (var sut = new RenderThrottle(
            () =>
            {
                Interlocked.Increment(ref renders);
                return Task.CompletedTask;
            },
            Interval,
            _time))
        {
            sut.Request();
        }

        _time.Advance(Interval * 4);
        await Task.Yield();

        // Assert
        renders.Should().Be(0);
    }
}
