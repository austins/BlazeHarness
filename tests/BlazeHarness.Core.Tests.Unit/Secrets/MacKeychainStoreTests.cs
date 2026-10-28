using BlazeHarness.Core.Secrets;
using BlazeHarness.Tests.Shared;

namespace BlazeHarness.Core.Tests.Unit.Secrets;

public sealed class MacKeychainStoreTests
{
    [Theory]
    [InlineData("a\nadd-generic-password -s x")]
    [InlineData("a\rb")]
    public async Task SetAsync_ValueWithLineBreak_ThrowsBeforeRunningAnything(string value)
    {
        // Arrange
        using var sut = new MacKeychainStore();

        // Act
        var act = async () => await sut.SetAsync("k", value, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>().WaitAsync(TestTimeouts.Default, TestContext.Current.CancellationToken);
    }
}
