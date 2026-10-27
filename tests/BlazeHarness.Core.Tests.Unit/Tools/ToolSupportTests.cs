using BlazeHarness.Core.Tools;

namespace BlazeHarness.Core.Tests.Unit.Tools;

public sealed class ToolSupportTests
{
    [Theory]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0x61 })]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x61, 0x00 })]
    [InlineData(new byte[] { 0xFE, 0xFF, 0x00, 0x61 })]
    [InlineData(new byte[] { 0x00, 0x00, 0xFE, 0xFF, 0x00, 0x00, 0x00, 0x61 })]
    public void LooksBinary_TextWithAByteOrderMark_IsText(byte[] bytes)
    {
        // Act
        var result = ToolSupport.LooksBinary(bytes);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void LooksBinary_ZeroByteWithoutAByteOrderMark_IsBinary()
    {
        // Act
        var result = ToolSupport.LooksBinary([0x61, 0x00, 0x62]);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void LooksBinary_ZeroByteBeyondTheSample_IsText()
    {
        // Arrange
        var bytes = new byte[9000];
        Array.Fill(bytes, (byte)'a');
        bytes[8500] = 0;

        // Act
        var result = ToolSupport.LooksBinary(bytes);

        // Assert
        result.Should().BeFalse();
    }
}
