using BlazeHarness.Core.Tools;

namespace BlazeHarness.Core.Tests.Unit.Tools;

public sealed class SendToChatPlanTests
{
    [Fact]
    public void Reason_ExistingChat_NamesItByTitleAndId()
    {
        // Arrange
        var sut = new SendToChatPlan("abc", "Story 2", "/work", SameScope: true, "hello");

        // Act
        var reason = sut.Reason;

        // Assert
        reason.Should().Contain("\"Story 2\"").And.Contain("chat:abc").And.Contain("/work").And.NotContain("hello");
    }

    [Fact]
    public void Reason_TwoChatsWithTheSameTitle_DiffersSoAnApprovalIsForOneOfThem()
    {
        // Arrange
        var first = new SendToChatPlan("abc", "Story 2", "/work", SameScope: true, "hello");
        var second = first with { ChatId = "def" };

        // Act
        var same = first.Reason == second.Reason;

        // Assert
        same.Should().BeFalse();
    }

    [Fact]
    public void Reason_NewChat_SaysItCreatesOne()
    {
        // Arrange
        var sut = new SendToChatPlan(null, null, null, SameScope: false, "hello");

        // Act
        var reason = sut.Reason;

        // Assert
        reason.Should().Contain("a new chat in no folder");
    }
}
