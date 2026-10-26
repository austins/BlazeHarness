using BlazeHarness.Core.Tools;

namespace BlazeHarness.Core.Tests.Unit.Tools;

public sealed class GlobTests
{
    [Theory]
    [InlineData("*.cs", "Program.cs", true)]
    [InlineData("*.cs", "src/app/Program.cs", true)]
    [InlineData("*.cs", "Program.csproj", false)]
    [InlineData("src/*.cs", "src/Program.cs", true)]
    [InlineData("src/*.cs", "src/app/Program.cs", false)]
    [InlineData("src/**/*.cs", "src/Program.cs", true)]
    [InlineData("src/**/*.cs", "src/a/b/Program.cs", true)]
    [InlineData("src/**", "src/a/b.txt", true)]
    [InlineData("**/test", "a/b/test", true)]
    [InlineData("file?.txt", "file1.txt", true)]
    [InlineData("file?.txt", "file12.txt", false)]
    [InlineData("[ab].txt", "a.txt", true)]
    [InlineData("[!ab].txt", "a.txt", false)]
    [InlineData("[!ab].txt", "c.txt", true)]
    [InlineData("a+b.txt", "a+b.txt", true)]
    [InlineData("./src/*.cs", "src/Program.cs", true)]
    public void ForTool_Pattern_MatchesAsExpected(string pattern, string path, bool expected)
    {
        // Act
        var result = Glob.ForTool(pattern).IsMatch(path);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void ForTool_UnclosedSet_MatchesTheBracketLiterally()
    {
        // Act
        var result = Glob.ForTool("a[b").IsMatch("a[b");

        // Assert
        result.Should().BeTrue();
    }
}
