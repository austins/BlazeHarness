using BlazeHarness.Web.Services;

namespace BlazeHarness.Web.Tests.Unit.Services;

public sealed class SidePanelMemoryTests
{
    private readonly SidePanelMemory _sut = new();

    [Fact]
    public void For_SameChat_ReturnsSameState()
    {
        // Arrange
        var first = _sut.For("a");
        first.OpenTab = SidePanelTab.Changes;

        // Act
        var again = _sut.For("a");
        var other = _sut.For("b");

        // Assert
        again.Should().BeSameAs(first);
        other.Should().NotBeSameAs(first);
        other.OpenTab.Should().BeNull();
    }

    [Fact]
    public void Adopt_NewChatGetsId_KeepsItsState()
    {
        // Arrange
        var memory = new ChatPanelMemory();

        // Act
        _sut.Adopt("new", memory);

        // Assert
        _sut.For("new").Should().BeSameAs(memory);
    }

    [Fact]
    public void For_MoreChatsThanCapacity_ForgetsLeastRecentlyUsed()
    {
        // Arrange
        var oldest = _sut.For("oldest");
        var used = _sut.For("used");
        for (var i = 0; i < SidePanelMemory.Capacity - 2; i++)
        {
            _sut.For($"chat{i}");
        }

        _sut.For("used");

        // Act
        _sut.For("one more");

        // Assert
        _sut.For("used").Should().BeSameAs(used);
        _sut.For("oldest").Should().NotBeSameAs(oldest);
    }
}
