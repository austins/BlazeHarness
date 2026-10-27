using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Sessions;
using System.Text.Json;

namespace BlazeHarness.Core.Tests.Unit.Sessions;

public sealed class SessionExportTests
{
    [Fact]
    public void Export_Markdown_WritesTheConversationWithToolCallsCollapsed()
    {
        // Arrange
        var info = SampleChat.Info();

        // Act
        var file = SessionExport.Export(info, SampleChat.Events(), ExportFormat.Markdown, SampleChat.Time);

        // Assert
        file.FileName.Should().Be("fix-the-build.md");
        file.ContentType.Should().Be("text/markdown");
        file.Content.Should().StartWith("# Fix the build\n\nStarted 2026-03-04 10:30 UTC\n\n## You\n\nWhy does it fail?\n\n*Attached: log.txt*\n\n");
        file.Content.Should().Contain("## Assistant · P1 / fast");
        file.Content.Should().Contain("<details>\n<summary>Thinking</summary>\n\nLet me look.\n\n</details>");
        file.Content.Should().Contain("<summary><code>read_file</code> build.sh · Done</summary>");
        file.Content.Should().Contain("A missing file.");
    }

    [Fact]
    public void Export_Markdown_ToolOutputWithBackticks_IsFencedLonger()
    {
        // Arrange
        var info = SampleChat.Info();

        // Act
        var content = SessionExport.Export(info, SampleChat.Events(), ExportFormat.Markdown, SampleChat.Time).Content;

        // Assert
        content.Should().Contain("````text\necho ```hi```\n````");
    }

    [Fact]
    public void Export_Markdown_ToolArguments_ArePrettyPrinted()
    {
        // Arrange
        var info = SampleChat.Info();

        // Act
        var content = SessionExport.Export(info, SampleChat.Events(), ExportFormat.Markdown, SampleChat.Time).Content;

        // Assert
        content.Should().Contain("```json\n{\n  \"path\": \"build.sh\"\n}\n```");
    }

    [Fact]
    public void Export_Markdown_AfterRewind_LeavesOutTheRemovedMessages()
    {
        // Arrange
        var events = SampleChat.Events();
        events.Add(new RewindEvent(SampleChat.Time, Guid.Empty));
        events.Add(new UserMessageEvent(SampleChat.Time, SampleChat.SecondUser, "Start over"));

        // Act
        var content = SessionExport.Export(SampleChat.Info(), events, ExportFormat.Markdown, SampleChat.Time).Content;

        // Assert
        content.Should().Contain("Start over");
        content.Should().NotContain("Why does it fail?");
        content.Should().NotContain("read_file");
    }

    [Fact]
    public void Export_Markdown_AfterCompaction_ShowsTheSummaryBeforeTheKeptMessages()
    {
        // Arrange
        var events = SampleChat.Events();
        events.Add(new UserMessageEvent(SampleChat.Time, SampleChat.SecondUser, "Next question"));
        events.Add(new CompactionEvent(SampleChat.Time, Guid.NewGuid(), SampleChat.SecondUser, "We fixed the build.", 900, 100));

        // Act
        var content = SessionExport.Export(SampleChat.Info(), events, ExportFormat.Markdown, SampleChat.Time).Content;

        // Assert
        content.Should().Contain("<summary>Summary</summary>\n\nWe fixed the build.");
        content.IndexOf("We fixed the build.", StringComparison.Ordinal).Should().BeLessThan(content.IndexOf("Next question", StringComparison.Ordinal));
        content.IndexOf("A missing file.", StringComparison.Ordinal).Should().BeLessThan(content.IndexOf("We fixed the build.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(EntryStatus.Failed, "> The reply failed: boom")]
    [InlineData(EntryStatus.Cancelled, "> The reply was stopped.")]
    public void Export_Markdown_UnfinishedReply_SaysSo(EntryStatus status, string note)
    {
        // Arrange
        var id = Guid.NewGuid();
        List<SessionEvent> events =
        [
            new AssistantStartEvent(SampleChat.Time, id, null),
            new AssistantEndEvent(SampleChat.Time, id, status, "boom", null),
        ];

        // Act
        var content = SessionExport.Export(SampleChat.Info(), events, ExportFormat.Markdown, SampleChat.Time).Content;

        // Assert
        content.Should().Contain(note);
    }

    [Fact]
    public void Export_Markdown_ReplyWithoutEnd_SaysItWasCutOff()
    {
        // Arrange
        List<SessionEvent> events = [new AssistantStartEvent(SampleChat.Time, Guid.NewGuid(), null)];

        // Act
        var content = SessionExport.Export(SampleChat.Info(), events, ExportFormat.Markdown, SampleChat.Time).Content;

        // Assert
        content.Should().Contain("> The reply was cut off.");
    }

    [Theory]
    [InlineData("Fix the build!", "fix-the-build.md")]
    [InlineData("  ../../Étude: a/b  ", "tude-a-b.md")]
    [InlineData("日本語", "chat.md")]
    [InlineData("", "chat.md")]
    public void Export_Title_BecomesASafeFileName(string title, string expected)
    {
        // Arrange
        var info = SampleChat.Info(title);

        // Act
        var file = SessionExport.Export(info, SampleChat.Events(), ExportFormat.Markdown, SampleChat.Time);

        // Assert
        file.FileName.Should().Be(expected);
    }

    [Fact]
    public void Export_LongTitle_GivesAShortFileName()
    {
        // Arrange
        var info = SampleChat.Info(new string('a', 500));

        // Act
        var file = SessionExport.Export(info, SampleChat.Events(), ExportFormat.Markdown, SampleChat.Time);

        // Assert
        file.FileName.Should().HaveLength(53);
    }

    [Fact]
    public void Export_Json_HoldsTheEventsAndLeavesOutMachineSpecificSettings()
    {
        // Arrange
        var info = SampleChat.Info();

        // Act
        var file = SessionExport.Export(info, SampleChat.Events(), ExportFormat.Json, SampleChat.Time);

        // Assert
        file.FileName.Should().Be("fix-the-build.json");
        file.ContentType.Should().Be("application/json");
        using var document = JsonDocument.Parse(file.Content);
        var root = document.RootElement;
        root.GetProperty("format").GetString().Should().Be("blazeharness-chat");
        root.GetProperty("version").GetInt32().Should().Be(1);
        root.GetProperty("session").GetProperty("title").GetString().Should().Be("Fix the build");
        root.GetProperty("events").GetArrayLength().Should().Be(8);
        file.Content.Should().NotContain("secret-project");
        file.Content.Should().NotContain("Unrestricted");
    }
}
