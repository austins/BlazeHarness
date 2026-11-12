using BlazeHarness.Web.Services;

namespace BlazeHarness.Web.Tests.Unit.Services;

public sealed class SettingsDialogStateTests
{
    private readonly SettingsDialogState _sut = new();
    private int _changes;

    public SettingsDialogStateTests()
    {
        _sut.Changed += () =>
        {
            _changes++;
            return Task.CompletedTask;
        };
    }

    [Fact]
    public async Task OpenAsync_NoSection_ShowsGeneral()
    {
        // Act
        await _sut.OpenAsync();

        // Assert
        _sut.Section.Should().Be(SettingsSection.General);
        _changes.Should().Be(1);
    }

    [Fact]
    public async Task OpenAsync_OtherSection_SwitchesSection()
    {
        // Arrange
        await _sut.OpenAsync();

        // Act
        await _sut.OpenAsync(SettingsSection.Mcp);

        // Assert
        _sut.Section.Should().Be(SettingsSection.Mcp);
    }

    [Fact]
    public async Task CloseAsync_WhileClosed_RaisesNothing()
    {
        // Act
        await _sut.CloseAsync();

        // Assert
        _changes.Should().Be(0);
    }

    [Fact]
    public async Task CloseAsync_WhileOpen_ClearsSection()
    {
        // Arrange
        await _sut.OpenAsync();

        // Act
        await _sut.CloseAsync();

        // Assert
        _sut.Section.Should().BeNull();
        _changes.Should().Be(2);
    }

    [Fact]
    public async Task OpenAsync_SlowHandler_CompletesAfterEveryHandler()
    {
        // Arrange
        var gate = new TaskCompletionSource();
        var secondRan = false;
#pragma warning disable VSTHRD003 // The test holds the handler open with its own gate.
        _sut.Changed += async () => await gate.Task;
#pragma warning restore VSTHRD003
        _sut.Changed += () =>
        {
            secondRan = true;
            return Task.CompletedTask;
        };

        // Act
        var open = _sut.OpenAsync();

        // Assert
        secondRan.Should().BeTrue();
        open.IsCompleted.Should().BeFalse();
        gate.SetResult();
        await open;
    }

    [Fact]
    public async Task OpenAsync_HandlerThrows_FaultsTheReturnedTask()
    {
        // Arrange
        _sut.Changed += () => Task.FromException(new InvalidOperationException());

        // Act
        var open = () => _sut.OpenAsync();

        // Assert
        await open.Should().ThrowAsync<InvalidOperationException>();
    }
}
