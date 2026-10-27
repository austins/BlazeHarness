using BlazeHarness.Web.Services;
using Microsoft.Extensions.Time.Testing;

namespace BlazeHarness.Web.Tests.Unit.Services;

public sealed class RenderThrottleTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(50);

    private readonly FakeTimeProvider _time = new();
    private readonly LoggerFake<RenderThrottle> _logger = new();

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
            _logger,
            _time);

        // Act
        for (var i = 0; i < 100; i++)
        {
            sut.Request();
        }

        _time.Advance(Interval);
        await rendered.Task.WaitAsync(TestTimeouts.Default, TestContext.Current.CancellationToken);
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
            _logger,
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
            _logger,
            _time);

        // Act
        sut.Request();
        _time.Advance(Interval);
        await first.Task.WaitAsync(TestTimeouts.Default, TestContext.Current.CancellationToken);
        sut.Request();
        _time.Advance(Interval);

        // Assert
        await second.Task.WaitAsync(TestTimeouts.Default, TestContext.Current.CancellationToken);
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
            _logger,
            _time))
        {
            sut.Request();
        }

        _time.Advance(Interval * 4);
        await Task.Yield();

        // Assert
        renders.Should().Be(0);
    }

    [Fact]
    public async Task Request_RenderThrows_LogsTheExceptionAndRendersAgainOnTheNextRequest()
    {
        // Arrange
        var renders = 0;
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sut = new RenderThrottle(
            () =>
            {
                if (Interlocked.Increment(ref renders) == 1)
                {
                    failed.TrySetResult();
                    throw new InvalidOperationException("secret text");
                }

                second.TrySetResult();
                return Task.CompletedTask;
            },
            Interval,
            _logger,
            _time);

        // Act
        sut.Request();
        _time.Advance(Interval);
        await failed.Task.WaitAsync(TestTimeouts.Default, TestContext.Current.CancellationToken);
        SpinWait.SpinUntil(() => _logger.Entries.Count > 0, TestTimeouts.Default);
        sut.Request();
        _time.Advance(Interval);
        await second.Task.WaitAsync(TestTimeouts.Default, TestContext.Current.CancellationToken);

        // Assert
        _logger.Entries.Should().ContainSingle().Which.Exception.Should().BeOfType<InvalidOperationException>();
    }
}
