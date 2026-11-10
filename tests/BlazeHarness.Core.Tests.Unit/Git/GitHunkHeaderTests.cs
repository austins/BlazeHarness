using BlazeHarness.Core.Git;

namespace BlazeHarness.Core.Tests.Unit.Git;

public sealed class GitHunkHeaderTests
{
    [Theory]
    [InlineData("@@ -1,3 +1,4 @@", 1, 3, 1, 4)]
    [InlineData("@@ -12,5 +14,6 @@ void M()", 12, 5, 14, 6)]
    [InlineData("@@ -10 +10,2 @@", 10, 1, 10, 2)]
    [InlineData("@@ -3 +2,0 @@", 3, 1, 2, 0)]
    [InlineData("@@ -2,0 +3 @@", 2, 0, 3, 1)]
    [InlineData("@@ -0,0 +1,3 @@", 0, 0, 1, 3)]
    public void TryParse_ValidHeader_ReadsBothRanges(string line, int oldStart, int oldCount, int newStart, int newCount)
    {
        // Act
        var ok = GitHunkHeader.TryParse(line, out var header);

        // Assert
        ok.Should().BeTrue();
        header.Should().Be(new GitHunkHeader(oldStart, oldCount, newStart, newCount));
    }

    [Theory]
    [InlineData("")]
    [InlineData("@@")]
    [InlineData("@@ -1,3 +1,4")]
    [InlineData("@@ +1,3 -1,4 @@")]
    [InlineData("@@ -a,3 +1,4 @@")]
    [InlineData("@@ -1,x +1,4 @@")]
    [InlineData("@@ -1 @@")]
    [InlineData(" -1,3 +1,4 @@")]
    public void TryParse_InvalidHeader_ReturnsFalse(string line)
    {
        // Act
        var ok = GitHunkHeader.TryParse(line, out _);

        // Assert
        ok.Should().BeFalse();
    }
}
