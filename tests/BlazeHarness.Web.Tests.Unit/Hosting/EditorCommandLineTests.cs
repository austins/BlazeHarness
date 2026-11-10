using BlazeHarness.Web.Hosting;

namespace BlazeHarness.Web.Tests.Unit.Hosting;

public sealed class EditorCommandLineTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Split_Blank_ReturnsEmpty(string? command)
    {
        // Act
        var parts = EditorCommandLine.Split(command);

        // Assert
        parts.Should().BeEmpty();
    }

    [Fact]
    public void Split_ProgramWithArguments_SplitsAtSpaces()
    {
        // Act
        var parts = EditorCommandLine.Split("  code  -g --new-window ");

        // Assert
        parts.Should().Equal("code", "-g", "--new-window");
    }

    [Fact]
    public void Split_QuotedWindowsPath_KeepsItTogetherWithItsBackslashes()
    {
        // Act
        var parts = EditorCommandLine.Split(@"""C:\Program Files\Notepad++\notepad++.exe"" -multiInst 'a b'");

        // Assert
        parts.Should().Equal(@"C:\Program Files\Notepad++\notepad++.exe", "-multiInst", "a b");
    }

    [Fact]
    public void Split_EmptyQuotes_KeepsAnEmptyArgument()
    {
        // Act
        var parts = EditorCommandLine.Split("edit \"\"");

        // Assert
        parts.Should().Equal("edit", string.Empty);
    }
}
