using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Attachments;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Providers;
using BlazeHarness.Core.Sessions;

namespace BlazeHarness.Core.Tests.Unit.Sessions;

/// <summary>
/// A small chat with every kind of event, for the export and import tests.
/// </summary>
internal static class SampleChat
{
    public static readonly DateTimeOffset Time = new(2026, 3, 4, 10, 30, 0, TimeSpan.Zero);

    public static readonly Guid FirstUser = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public static readonly Guid FirstReply = Guid.Parse("00000000-0000-0000-0000-000000000002");
    public static readonly Guid SecondUser = Guid.Parse("00000000-0000-0000-0000-000000000003");
    public static readonly Guid SecondReply = Guid.Parse("00000000-0000-0000-0000-000000000004");

    public static SessionInfo Info(string title = "Fix the build") => new()
    {
        Id = "abc",
        Title = title,
        TitleSource = TitleSource.Generated,
        CreatedAt = Time,
        UpdatedAt = Time.AddMinutes(5),
        FolderPath = "/home/me/secret-project",
        ProviderId = "p1",
        ModelId = "fast",
        Level = ThinkingLevel.Low,
        Access = AccessLevel.Unrestricted,
        Approvals = ApprovalMode.NeverAsk,
    };

    public static List<SessionEvent> Events() =>
    [
        new UserMessageEvent(
            Time,
            FirstUser,
            "Why does it fail?",
            [new SentAttachment(new AttachmentInfo("a1", "log.txt", "text/plain", 10, 1), AttachmentDelivery.Inline)]),
        new AssistantStartEvent(Time, FirstReply, "P1 / fast"),
        new AssistantDeltaEvent(Time, FirstReply, string.Empty, "Let me look."),
        new ToolCallEvent(Time, FirstReply, "c1", "read_file", """{"path":"build.sh"}""", "build.sh"),
        new ToolResultEvent(Time, FirstReply, "c1", ToolCallStatus.Succeeded, "echo ```hi```\n", "/etc/passwd"),
        new AssistantDeltaEvent(Time, FirstReply, "A missing ", string.Empty),
        new AssistantDeltaEvent(Time, FirstReply, "file.", string.Empty),
        new AssistantEndEvent(Time, FirstReply, EntryStatus.Done, null, new UsageReported(100, 20, 0)),
    ];
}
