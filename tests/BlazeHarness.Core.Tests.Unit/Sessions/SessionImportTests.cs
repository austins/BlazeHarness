using BlazeHarness.Core.Providers;
using BlazeHarness.Core.Sessions;
using System.Text.Json;

namespace BlazeHarness.Core.Tests.Unit.Sessions;

public sealed class SessionImportTests
{
    [Fact]
    public void TryParse_ExportedChat_RoundTrips()
    {
        // Arrange
        var json = SessionExport.ToJson(SampleChat.Info(), SampleChat.Events(), SampleChat.Time);

        // Act
        var ok = SessionImport.TryParse(json, out var session, out var error);

        // Assert
        ok.Should().BeTrue();
        error.Should().BeEmpty();
        session!.Info.Title.Should().Be("Fix the build");
        session.Info.TitleSource.Should().Be(TitleSource.Generated);
        session.Info.CreatedAt.Should().Be(SampleChat.Time);
        session.Info.ProviderId.Should().Be("p1");
        session.Info.ModelId.Should().Be("fast");
        session.Info.Level.Should().Be(ThinkingLevel.Low);
        session.Info.FolderPath.Should().BeNull();
        session.Events.Should().HaveCount(8);
        session.Events.OfType<AssistantDeltaEvent>().Select(d => d.Text + d.Reasoning).Should().Equal("Let me look.", "A missing ", "file.");
    }

    [Fact]
    public void TryParse_ImportedChat_DropsAttachmentsAndOutputFilePaths()
    {
        // Arrange
        var json = SessionExport.ToJson(SampleChat.Info(), SampleChat.Events(), SampleChat.Time);

        // Act
        var ok = SessionImport.TryParse(json, out var session, out _);

        // Assert
        ok.Should().BeTrue();
        session!.Events.OfType<UserMessageEvent>().Should().OnlyContain(u => u.Attachments == null);
        var result = session.Events.OfType<ToolResultEvent>().Single();
        result.OutputFile.Should().BeNull();
        result.Output.Should().Be("echo ```hi```\n");
    }

    [Fact]
    public void TryParse_LongTitle_IsCut()
    {
        // Arrange
        var json = SessionExport.ToJson(SampleChat.Info(new string('x', 5000)), SampleChat.Events(), SampleChat.Time);

        // Act
        var ok = SessionImport.TryParse(json, out var session, out _);

        // Assert
        ok.Should().BeTrue();
        session!.Info.Title.Should().HaveLength(200);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"format":"other","version":1,"session":{},"events":[]}""")]
    [InlineData("""{"format":"blazeharness-chat","version":1,"session":{"title":"x"}}""")]
    [InlineData("""{"format":"blazeharness-chat","version":1,"session":{"title":"x"},"events":[{"type":"nope"}]}""")]
    public void TryParse_NotAChatExport_IsRejected(string json)
    {
        // Act
        var ok = SessionImport.TryParse(json, out var session, out var error);

        // Assert
        ok.Should().BeFalse();
        session.Should().BeNull();
        error.Should().NotBeEmpty();
    }

    [Fact]
    public void TryParse_NewerVersion_IsRejectedWithAClearMessage()
    {
        // Arrange
        using var document = JsonDocument.Parse(SessionExport.ToJson(SampleChat.Info(), SampleChat.Events(), SampleChat.Time));
        var json = document.RootElement.GetRawText().Replace("\"version\": 1", "\"version\": 2", StringComparison.Ordinal);

        // Act
        var ok = SessionImport.TryParse(json, out _, out var error);

        // Assert
        ok.Should().BeFalse();
        error.Should().Contain("newer version");
    }

    [Fact]
    public void TryParse_NoMessages_IsRejected()
    {
        // Arrange
        var json = SessionExport.ToJson(SampleChat.Info(), [new RewindEvent(SampleChat.Time, Guid.Empty)], SampleChat.Time);

        // Act
        var ok = SessionImport.TryParse(json, out _, out var error);

        // Assert
        ok.Should().BeFalse();
        error.Should().Be("The file has no messages.");
    }
}
