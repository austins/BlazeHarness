using BlazeHarness.Web.Services;

namespace BlazeHarness.Web.Tests.Unit.Services;

public sealed class RenderThrottleTests
{
    [Fact]
    public async Task Request_ManyCallsInOneInterval_RendersOnce()
    {
        // Arrange
        var renders = 0;
        var rendered = new TaskCompletionSource();
        using var sut = new RenderThrottle(
            () =>
            {
                Interlocked.Increment(ref renders);
                rendered.TrySetResult();
                return Task.CompletedTask;
            },
            TimeSpan.FromMilliseconds(30));

        // Act
        for (var i = 0; i < 100; i++)
        {
            sut.Request();
        }

        await rendered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert
        renders.Should().Be(1);
    }

    [Fact]
    public async Task Request_AfterARender_SchedulesAnotherRender()
    {
        // Arrange
        var renders = 0;
        var second = new TaskCompletionSource();
        using var sut = new RenderThrottle(
            () =>
            {
                if (Interlocked.Increment(ref renders) == 2)
                {
                    second.TrySetResult();
                }

                return Task.CompletedTask;
            },
            TimeSpan.FromMilliseconds(10));

        // Act
        sut.Request();
        await Task.Delay(100, TestContext.Current.CancellationToken);
        sut.Request();

        // Assert
        await second.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
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
            TimeSpan.FromMilliseconds(50)))
        {
            sut.Request();
        }

        await Task.Delay(150, TestContext.Current.CancellationToken);

        // Assert
        renders.Should().Be(0);
    }
}
