using BlazeHarness.Core.Attachments;

namespace BlazeHarness.Core.Tests.Unit.Attachments;

public sealed class FileSizeTests
{
    [Theory]
    [InlineData(500L, "500 bytes")]
    [InlineData(2048L, "2 KB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(3L * 1024 * 1024, "3 MB")]
    public void Format_Bytes_ReadsNaturally(long bytes, string expected)
    {
        // Act
        var text = FileSize.Format(bytes);

        // Assert
        text.Should().Be(expected);
    }
}
