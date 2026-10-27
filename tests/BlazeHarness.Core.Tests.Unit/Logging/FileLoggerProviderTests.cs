using BlazeHarness.Core.Logging;
using Microsoft.Extensions.Time.Testing;

namespace BlazeHarness.Core.Tests.Unit.Logging;

public sealed class FileLoggerProviderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bh-log-" + Guid.NewGuid().ToString("N"), "logs");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 10, 8, 30, 15, 123, TimeSpan.Zero));

    public void Dispose()
    {
        var root = Path.GetDirectoryName(_directory)!;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Log_Warning_WritesALineInTodaysFileAndCreatesTheFolder()
    {
        // Arrange
        using var sut = new FileLoggerProvider(_directory, _time);
        var logger = sut.CreateLogger("Some.Category");

        // Act
        Write(logger, LogLevel.Warning, "Could not read config.json.");

        // Assert
        var text = await File.ReadAllTextAsync(Path.Combine(_directory, "blazeharness-20260310.log"), TestContext.Current.CancellationToken);
        text.Should().Be("2026-03-10T08:30:15.123Z WRN Some.Category Could not read config.json.\n");
    }

    [Fact]
    public async Task Log_Exception_WritesItsTypeButNotItsMessage()
    {
        // Arrange
        using var sut = new FileLoggerProvider(_directory, _time);
        var logger = sut.CreateLogger("C");

        // Act
        Write(logger, LogLevel.Error, "Failed", new InvalidOperationException("secret prompt text"));

        // Assert
        var text = await File.ReadAllTextAsync(Path.Combine(_directory, "blazeharness-20260310.log"), TestContext.Current.CancellationToken);
        text.Should().StartWith("2026-03-10T08:30:15.123Z ERR C Failed\nSystem.InvalidOperationException\n").And.NotContain("secret prompt text");
    }

    [Fact]
    public async Task Log_MessageWithNewLines_StaysOnOneLine()
    {
        // Arrange
        using var sut = new FileLoggerProvider(_directory, _time);
        var logger = sut.CreateLogger("C");

        // Act
        Write(logger, LogLevel.Information, "one\r\ntwo");

        // Assert
        var text = await File.ReadAllTextAsync(Path.Combine(_directory, "blazeharness-20260310.log"), TestContext.Current.CancellationToken);
        text.Should().EndWith("INF C one two\n");
    }

    [Fact]
    public async Task Log_NextDay_StartsANewFile()
    {
        // Arrange
        using var sut = new FileLoggerProvider(_directory, _time);
        var logger = sut.CreateLogger("C");
        Write(logger, LogLevel.Information, "today");

        // Act
        _time.Advance(TimeSpan.FromDays(1));
        Write(logger, LogLevel.Information, "tomorrow");

        // Assert
        Directory.GetFiles(_directory).Select(Path.GetFileName).Order(StringComparer.Ordinal)
            .Should().Equal("blazeharness-20260310.log", "blazeharness-20260311.log");
        (await File.ReadAllTextAsync(Path.Combine(_directory, "blazeharness-20260311.log"), TestContext.Current.CancellationToken)).Should().Contain("tomorrow").And.NotContain("today");
    }

    [Fact]
    public void Log_FirstEntryOfADay_DeletesFilesPastTheRetentionOnly()
    {
        // Arrange
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "blazeharness-20260303.log"), "old");
        File.WriteAllText(Path.Combine(_directory, "blazeharness-20260304.log"), "kept");
        File.WriteAllText(Path.Combine(_directory, "other.log"), "not ours");
        using var sut = new FileLoggerProvider(_directory, _time);

        // Act
        Write(sut.CreateLogger("C"), LogLevel.Information, "now");

        // Assert
        Directory.GetFiles(_directory).Select(Path.GetFileName).Order(StringComparer.Ordinal)
            .Should().Equal("blazeharness-20260304.log", "blazeharness-20260310.log", "other.log");
    }

    [Fact]
    public void Log_FileAtTheSizeCap_IsNotGrown()
    {
        // Arrange
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "blazeharness-20260310.log");
        using (var file = File.Create(path))
        {
            file.SetLength(FileLoggerProvider.MaxFileBytes);
        }

        using var sut = new FileLoggerProvider(_directory, _time);

        // Act
        Write(sut.CreateLogger("C"), LogLevel.Error, "more");

        // Assert
        new FileInfo(path).Length.Should().Be(FileLoggerProvider.MaxFileBytes);
    }

    [Fact]
    public async Task Log_ExceptionWithStatusCode_WritesTheStatusAndInnerType()
    {
        // Arrange
        using var sut = new FileLoggerProvider(_directory, _time);
        var failure = new InvalidOperationException("outer", new HttpRequestException("echoed body", null, System.Net.HttpStatusCode.TooManyRequests));

        // Act
        Write(sut.CreateLogger("C"), LogLevel.Error, "Failed", failure);

        // Assert
        var text = await File.ReadAllTextAsync(Path.Combine(_directory, "blazeharness-20260310.log"), TestContext.Current.CancellationToken);
        text.Should().Contain("System.Net.Http.HttpRequestException (HTTP 429)").And.NotContain("echoed body");
    }

    [Fact]
    public async Task Log_EntryFillsTheFile_WritesOneFullMarkerAndDropsTheRest()
    {
        // Arrange
        using var sut = new FileLoggerProvider(_directory, _time);
        var logger = sut.CreateLogger("C");

        // Act
        Write(logger, LogLevel.Information, new string('x', (int)FileLoggerProvider.MaxFileBytes));
        Write(logger, LogLevel.Error, "dropped one");
        Write(logger, LogLevel.Error, "dropped two");

        // Assert
        var text = await File.ReadAllTextAsync(Path.Combine(_directory, "blazeharness-20260310.log"), TestContext.Current.CancellationToken);
        text.Should().NotContain("dropped one").And.NotContain("dropped two");
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Count(line => line.Contains("Log file is full", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public void Log_FolderCannotBeCreated_DoesNotThrow()
    {
        // Arrange
        Directory.CreateDirectory(Path.GetDirectoryName(_directory)!);
        File.WriteAllText(_directory, "a file where the folder should be");
        using var sut = new FileLoggerProvider(_directory, _time);

        // Act
        var act = () => Write(sut.CreateLogger("C"), LogLevel.Error, "lost");

        // Assert
        act.Should().NotThrow();
    }

    private static void Write(ILogger logger, LogLevel level, string message, Exception? exception = null)
    {
        logger.Log(level, 0, message, exception, (text, _) => text);
    }
}
