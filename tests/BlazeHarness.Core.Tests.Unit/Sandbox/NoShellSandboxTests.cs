using BlazeHarness.Core.Sandbox;
using BlazeHarness.Core.Tools;

namespace BlazeHarness.Core.Tests.Unit.Sandbox;

public sealed class NoShellSandboxTests
{
    private readonly NoShellSandbox _sut = new("No sandbox here.");

    [Fact]
    public void Status_Always_IsUnavailableWithTheReason()
    {
        // Assert
        _sut.Status.IsAvailable.Should().BeFalse();
        _sut.Status.Detail.Should().Be("No sandbox here.");
    }

    [Theory]
    [InlineData(ShellMode.Writable)]
    [InlineData(ShellMode.ReadOnly)]
    public async Task StartAsync_ConfinedWithoutASandbox_Throws(ShellMode mode)
    {
        // Arrange
        var launch = new ShellLaunch(new ShellInfo("sh", "/bin/sh", IsPowerShell: false), "true", "/work", mode, "/scratch", TimeSpan.FromSeconds(5));

        // Act
        var act = () => _sut.StartAsync(launch, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("No sandbox here.");
    }

    [Fact]
    public void Constructor_BlankReason_Throws()
    {
        // Act
        var act = () => new NoShellSandbox(" ");

        // Assert
        act.Should().Throw<ArgumentException>();
    }
}
