using BlazeHarness.Tests.Shared;
using ChatLog = BlazeHarness.Core.Chats.ChatLog;

namespace BlazeHarness.Core.Tests.Integration.Chats;

public sealed class ChatLogLoggingTests : IDisposable
{
    private const string Secret = "sk-SECRET-conversation-text";

    private readonly TempFolder _temp = new("bh-log-leak-");

    [Fact]
    public async Task ReadAsync_CorruptLineHoldingPrivateText_LogsWithoutTheText()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(_temp.Root, "messages.jsonl");
        await File.WriteAllTextAsync(path, $$"""{"type":"user","text":"{{Secret}}" nope""" + "\n", ct);
        var logger = new LoggerFake<ChatLog>();
        using var sut = new ChatLog(path, logger);

        // Act
        var events = await sut.ReadAsync(ct);

        // Assert
        events.Should().BeEmpty();
        logger.Entries.Should().NotBeEmpty();
        logger.Entries.Should().OnlyContain(e => !e.Message.Contains(Secret) && (e.Exception == null || !e.Exception.ToString().Contains(Secret)));
    }

    public void Dispose() => _temp.Dispose();
}
