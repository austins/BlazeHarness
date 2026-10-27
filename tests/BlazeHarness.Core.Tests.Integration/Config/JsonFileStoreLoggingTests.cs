using BlazeHarness.Core.Config;

namespace BlazeHarness.Core.Tests.Integration.Config;

public sealed class JsonFileStoreLoggingTests : IDisposable
{
    private const string Secret = "sk-SECRET-config-value";

    private readonly TempFolder _temp = new("bh-store-leak-");

    [Fact]
    public async Task Current_CorruptFileHoldingASecret_LogsWithoutTheSecret()
    {
        // Arrange
        var path = Path.Combine(_temp.Root, "config.json");
        await File.WriteAllTextAsync(path, $$"""{"apiKey": "{{Secret}}", broken""", TestContext.Current.CancellationToken);
        var logger = new LoggerFake<JsonFileStore<Dictionary<string, string>>>();

        // Act
        using var sut = new JsonFileStore<Dictionary<string, string>>(path, logger, watch: false);

        // Assert
        logger.Entries.Should().NotBeEmpty();
        logger.Entries.Should().OnlyContain(e => !e.Message.Contains(Secret) && (e.Exception == null || !e.Exception.ToString().Contains(Secret)));
    }

    public void Dispose() => _temp.Dispose();
}
