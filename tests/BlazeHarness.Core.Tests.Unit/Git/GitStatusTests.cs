using BlazeHarness.Core.Git;

namespace BlazeHarness.Core.Tests.Unit.Git;

public sealed class GitStatusTests
{
    private static readonly GitChangedFile A = new("a.txt", GitChangeKind.Modified, false, true, 1, 1, false, "1:2");

    private static readonly GitChangedFile B = new("b.txt", GitChangeKind.Added, true, false, 3, 0, false, "3:4");

    [Fact]
    public void IsSameAs_EqualStatusesWithSeparateLists_ReturnsTrue()
    {
        // Arrange
        var first = new GitStatus("/repo", [A, B], 2, 1, 1);
        var second = new GitStatus("/repo", [A with { }, B with { }], 2, 1, 1);

        // Act
        var result = first.IsSameAs(second);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsSameAs_Null_ReturnsFalse()
    {
        // Arrange
        var status = new GitStatus("/repo", [A], 1, 0, 1);

        // Act
        var result = status.IsSameAs(null);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsSameAs_DifferentRoot_ReturnsFalse()
    {
        // Act
        var result = new GitStatus("/repo", [A], 1, 0, 1).IsSameAs(new GitStatus("/other", [A], 1, 0, 1));

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(2, 0, 1)]
    [InlineData(1, 1, 1)]
    [InlineData(1, 0, 2)]
    public void IsSameAs_DifferentCount_ReturnsFalse(int all, int staged, int unstaged)
    {
        // Act
        var result = new GitStatus("/repo", [A], 1, 0, 1).IsSameAs(new GitStatus("/repo", [A], all, staged, unstaged));

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsSameAs_FileWithDifferentStamp_ReturnsFalse()
    {
        // Act
        var result = new GitStatus("/repo", [A], 1, 0, 1).IsSameAs(new GitStatus("/repo", [A with { Stamp = "9:9" }], 1, 0, 1));

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsSameAs_SameFilesInAnotherOrder_ReturnsFalse()
    {
        // Act
        var result = new GitStatus("/repo", [A, B], 2, 1, 1).IsSameAs(new GitStatus("/repo", [B, A], 2, 1, 1));

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsSameAs_FewerFiles_ReturnsFalse()
    {
        // Act
        var result = new GitStatus("/repo", [A, B], 2, 1, 1).IsSameAs(new GitStatus("/repo", [A], 2, 1, 1));

        // Assert
        result.Should().BeFalse();
    }
}
