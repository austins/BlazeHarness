using BlazeHarness.Core.Tools.Files;

namespace BlazeHarness.Core.Tests.Unit.Tools.Files;

public sealed class ReadFileToolTests
{
    [Fact]
    public void ReadLines_PageLimit_DoesNotReadPastTheLookahead()
    {
        // Arrange
        using var reader = Substitute.For<TextReader>();
        var reads = 0;
        reader.Read(Arg.Any<char[]>(), Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
        {
            if (++reads > 1)
            {
                throw new InvalidOperationException("The unread tail must not be scanned.");
            }

            const string Text = "first\nnext\n";
            Text.CopyTo(0, call.ArgAt<char[]>(0), call.ArgAt<int>(1), Text.Length);
            return Text.Length;
        });

        // Act
        var result = ReadFileTool.ReadLines("data.txt", reader, 1, 1, TestContext.Current.CancellationToken);

        // Assert
        result.Output.Should().Be("first\n[Showing lines 1-1. Call again with offset=2 to continue.]");
        reads.Should().Be(1);
    }
}
