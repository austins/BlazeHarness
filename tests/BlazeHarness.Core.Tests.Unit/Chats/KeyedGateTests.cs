using BlazeHarness.Core.Chats;
using BlazeHarness.Tests.Shared;

namespace BlazeHarness.Core.Tests.Unit.Chats;

public sealed class KeyedGateTests
{
    private readonly KeyedGate _sut = new();

    [Fact]
    public async Task EnterAsync_DifferentKeys_DoNotWaitForEachOther()
    {
        // Arrange
        using (await _sut.EnterAsync("a", TestContext.Current.CancellationToken))
        {
            // Act
            var other = _sut.EnterAsync("b", TestContext.Current.CancellationToken);

            // Assert
            other.IsCompletedSuccessfully.Should().BeTrue();
            using var lease = await other;
        }
    }

    [Fact]
    public async Task EnterAsync_SameKey_WaitsUntilTheLeaseIsDisposed()
    {
        // Arrange
        Task<KeyedGate.Lease> waiting;
        using (await _sut.EnterAsync("a", TestContext.Current.CancellationToken))
        {
            // Act
            waiting = _sut.EnterAsync("a", TestContext.Current.CancellationToken);

            // Assert
            waiting.IsCompleted.Should().BeFalse();
        }

        using var lease = await waiting.WaitAsync(TestTimeouts.Default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task EnterAsync_CancelledWait_LeavesTheKeyUsable()
    {
        // Arrange
        using (await _sut.EnterAsync("a", TestContext.Current.CancellationToken))
        {
            // Act
            var act = async () =>
            {
                using var lease = await _sut.EnterAsync("a", new CancellationToken(canceled: true));
            };

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        var again = _sut.EnterAsync("a", TestContext.Current.CancellationToken);
        again.IsCompletedSuccessfully.Should().BeTrue();
        using var lease = await again;
    }
}
