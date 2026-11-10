using BlazeHarness.Web.Hosting;

namespace BlazeHarness.Web.Tests.Unit.Hosting;

public sealed class DesktopEntryTests
{
    private const string Path = "/home/me/my project/a.cs";

    [Fact]
    public void CommandFor_FileFieldCode_PutsThePathThere()
    {
        // Arrange
        const string content = """
            [Desktop Entry]
            Name=Kate
            Exec=kate -b %U
            Terminal=false
            """;

        // Act
        var command = DesktopEntry.CommandFor(content, Path);

        // Assert
        command.Should().Equal("kate", "-b", Path);
    }

    [Fact]
    public void CommandFor_NoFileFieldCode_AddsThePathLast()
    {
        // Act
        var command = DesktopEntry.CommandFor("[Desktop Entry]\nExec=gedit --new-window", Path);

        // Assert
        command.Should().Equal("gedit", "--new-window", Path);
    }

    [Fact]
    public void CommandFor_OtherFieldCodes_DropsThemAndKeepsLiteralPercent()
    {
        // Act
        var command = DesktopEntry.CommandFor("[Desktop Entry]\nExec=edit %i %c %k --zoom=100%% %f %F", Path);

        // Assert
        command.Should().Equal("edit", "--zoom=100%", Path);
    }

    [Fact]
    public void CommandFor_QuotedProgram_KeepsSpacesAndEscapes()
    {
        // Act
        var command = DesktopEntry.CommandFor("[Desktop Entry]\nExec=\"/opt/my editor/run\" \"say \\\\\"hi\\\\\"\" %f", Path);

        // Assert
        command.Should().Equal("/opt/my editor/run", "say \"hi\"", Path);
    }

    [Fact]
    public void CommandFor_ExecInAnotherGroup_IsIgnored()
    {
        // Arrange
        const string content = """
            [Desktop Entry]
            Exec=kate %U

            [Desktop Action new-window]
            Exec=kate --new %U
            """;

        // Act
        var command = DesktopEntry.CommandFor(content, Path);

        // Assert
        command.Should().Equal("kate", Path);
    }

    [Fact]
    public void CommandFor_TerminalApp_ReturnsNull()
    {
        // Act
        var command = DesktopEntry.CommandFor("[Desktop Entry]\nExec=vim %F\nTerminal=true", Path);

        // Assert
        command.Should().BeNull();
    }

    [Fact]
    public void CommandFor_NoExec_ReturnsNull()
    {
        // Act
        var command = DesktopEntry.CommandFor("[Desktop Entry]\nName=Broken", Path);

        // Assert
        command.Should().BeNull();
    }
}
