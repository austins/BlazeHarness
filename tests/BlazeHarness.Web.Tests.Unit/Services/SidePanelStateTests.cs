using BlazeHarness.Web.Services;

namespace BlazeHarness.Web.Tests.Unit.Services;

public sealed class SidePanelStateTests
{
    private readonly SidePanelState _sut = new();
    private int _changes;

    public SidePanelStateTests()
    {
        _sut.Changed += (_, _) => _changes++;
    }

    [Fact]
    public void Current_NothingAvailable_IsNull()
    {
        // Assert
        _sut.Current.Should().BeNull();
        _sut.IsVisible.Should().BeFalse();
    }

    [Fact]
    public void Open_OnlyChangesAvailable_ShowsChanges()
    {
        // Arrange
        _sut.SetChangeCount(2);

        // Act
        _sut.Open();

        // Assert
        _sut.Current.Should().Be(SidePanelTab.Changes);
        _sut.IsVisible.Should().BeTrue();
    }

    [Fact]
    public void Current_SelectedTabHasNothing_FallsBackToTheOther()
    {
        // Arrange
        _sut.SetBrowser(true);
        _sut.SetChangeCount(1);
        _sut.Open(SidePanelTab.Changes);

        // Act
        _sut.SetChangeCount(0);

        // Assert
        _sut.Current.Should().Be(SidePanelTab.Browser);
    }

    [Fact]
    public void SetChangeCount_LastViewDisappears_ClosesSoItDoesNotReopenByItself()
    {
        // Arrange
        _sut.SetChangeCount(1);
        _sut.Open();

        // Act
        _sut.SetChangeCount(0);
        _sut.SetChangeCount(3);

        // Assert
        _sut.IsOpen.Should().BeFalse();
        _sut.IsVisible.Should().BeFalse();
    }

    [Fact]
    public void SetChangeCount_ChangesAppear_DoesNotOpenThePanel()
    {
        // Act
        _sut.SetChangeCount(4);

        // Assert
        _sut.HasChanges.Should().BeTrue();
        _sut.IsVisible.Should().BeFalse();
    }

    [Fact]
    public void Toggle_VisiblePanel_ClosesAndThenReopens()
    {
        // Arrange
        _sut.SetBrowser(true);
        _sut.Open();

        // Act
        _sut.Toggle();
        var closed = _sut.IsVisible;
        _sut.Toggle();

        // Assert
        closed.Should().BeFalse();
        _sut.IsVisible.Should().BeTrue();
    }

    [Fact]
    public void Open_AlreadyOpenOnThatView_RaisesNothing()
    {
        // Arrange
        _sut.SetBrowser(true);
        _sut.Open(SidePanelTab.Browser);
        var before = _changes;

        // Act
        _sut.Open(SidePanelTab.Browser);

        // Assert
        _changes.Should().Be(before);
    }

    [Fact]
    public void Reset_AfterUse_ForgetsEverything()
    {
        // Arrange
        _sut.SetBrowser(true);
        _sut.SetChangeCount(2);
        _sut.Open(SidePanelTab.Changes);

        // Act
        _sut.Reset();

        // Assert
        (_sut.IsOpen, _sut.HasBrowser, _sut.ChangeCount, _sut.Selected).Should().Be((false, false, 0, SidePanelTab.Browser));
    }

    [Fact]
    public void SetChangeCount_Negative_CountsAsNone()
    {
        // Act
        _sut.SetChangeCount(-3);

        // Assert
        _sut.ChangeCount.Should().Be(0);
    }
}
