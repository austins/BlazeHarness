using BlazeHarness.Web.Services;

namespace BlazeHarness.Web.Tests.Unit.Services;

public sealed class SettingsDialogStateTests
{
    private readonly SettingsDialogState _sut = new();
    private int _changes;

    public SettingsDialogStateTests()
    {
        _sut.Changed += (_, _) => _changes++;
    }

    [Fact]
    public void Open_NoSection_ShowsProviders()
    {
        // Act
        _sut.Open();

        // Assert
        _sut.Section.Should().Be(SettingsSection.Providers);
        _changes.Should().Be(1);
    }

    [Fact]
    public void Open_OtherSection_SwitchesSection()
    {
        // Arrange
        _sut.Open();

        // Act
        _sut.Open(SettingsSection.Mcp);

        // Assert
        _sut.Section.Should().Be(SettingsSection.Mcp);
    }

    [Fact]
    public void Close_WhileClosed_RaisesNothing()
    {
        // Act
        _sut.Close();

        // Assert
        _changes.Should().Be(0);
    }

    [Fact]
    public void Close_WhileOpen_ClearsSection()
    {
        // Arrange
        _sut.Open();

        // Act
        _sut.Close();

        // Assert
        _sut.Section.Should().BeNull();
        _changes.Should().Be(2);
    }
}
