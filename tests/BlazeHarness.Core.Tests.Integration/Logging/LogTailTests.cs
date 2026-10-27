using BlazeHarness.Core.Logging;

namespace BlazeHarness.Core.Tests.Integration.Logging;

public sealed class LogTailTests : IDisposable
{
    private readonly TempFolder _temp = new("bh-tail-");

    public LogTailTests()
    {
        Directory.CreateDirectory(_temp.Root);
    }

    public void Dispose()
    {
        _temp.Dispose();
    }

    [Fact]
    public async Task ReadAsync_NoFolder_ReturnsNothing()
    {
        // Act
        var lines = await LogTail.ReadAsync(Path.Combine(_temp.Root, "missing"), 10, TestContext.Current.CancellationToken);

        // Assert
        lines.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_SeveralFiles_ReadsTheLastLinesOfTheNewest()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Path.Combine(_temp.Root, "blazeharness-20260309.log"), "yesterday\n", ct);
        await File.WriteAllTextAsync(Path.Combine(_temp.Root, "blazeharness-20260310.log"), "one\ntwo\nthree\n", ct);

        // Act
        var lines = await LogTail.ReadAsync(_temp.Root, 2, ct);

        // Assert
        lines.Should().Equal("two", "three");
    }

    [Fact]
    public async Task ReadAsync_LargeFile_ReadsOnlyWholeLinesFromTheEnd()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var line = new string('x', 99) + "\n";
        await File.WriteAllTextAsync(Path.Combine(_temp.Root, "blazeharness-20260310.log"), string.Concat(Enumerable.Repeat(line, 10_000)) + "last\n", ct);

        // Act
        var lines = await LogTail.ReadAsync(_temp.Root, 100_000, ct);

        // Assert
        lines.Should().HaveCountLessThan(10_001);
        lines[^1].Should().Be("last");
        lines.Take(lines.Count - 1).Should().OnlyContain(l => l.Length == 99);
    }
}
