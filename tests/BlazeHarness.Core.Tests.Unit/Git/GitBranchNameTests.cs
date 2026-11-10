using BlazeHarness.Core.Git;

namespace BlazeHarness.Core.Tests.Unit.Git;

public sealed class GitBranchNameTests
{
    [Theory]
    [InlineData("main")]
    [InlineData("feature/login-page")]
    [InlineData("v1.2")]
    public void IsValid_GoodName_IsTrue(string name)
    {
        GitBranchName.IsValid(name).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("@")]
    [InlineData("-f")]
    [InlineData("a..b")]
    [InlineData("a b")]
    [InlineData("a~1")]
    [InlineData("a:b")]
    [InlineData("a\\b")]
    [InlineData("a@{1}")]
    [InlineData("a/")]
    [InlineData("a.")]
    [InlineData("a//b")]
    [InlineData("a/.b")]
    [InlineData("a.lock")]
    public void IsValid_BadName_IsFalse(string? name)
    {
        GitBranchName.IsValid(name).Should().BeFalse();
    }

    [Theory]
    [InlineData("Fix the Login page!", "fix-the-login-page")]
    [InlineData("  ---  ", "chat-1")]
    [InlineData(null, "chat-1")]
    [InlineData("Ünïcode only ✓", "n-code-only")]
    [InlineData("a very long title that goes on and on past the limit", "a-very-long-title-that-goes-on-and-on")]
    public void Suggest_Text_ReturnsBranchName(string? text, string expected)
    {
        // Act
        var name = GitBranchName.Suggest(text, "chat-1");

        // Assert
        name.Should().Be(expected);
        GitBranchName.IsValid(name).Should().BeTrue();
    }
}
