using BlazeHarness.Core.Tools;
using System.Text.Json;

namespace BlazeHarness.Core.Tests.Integration.Tools;

public sealed class ToolSetTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void Constructor_DuplicateNames_Throws()
    {
        // Act
        var act = () => new ToolSet([new ReadFileTool(), new ReadFileTool()], _workspace.Context);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*read_file*");
    }

    [Fact]
    public void Declarations_BuiltInTools_HaveNamesAndSchemas()
    {
        // Act
        var sut = new ToolSet([new ReadFileTool(), new WriteFileTool(), new EditFileTool(), new ListFilesTool(), new SearchFilesTool()], _workspace.Context);

        // Assert
        sut.Declarations.Select(d => d.Name).Should().Equal("read_file", "write_file", "edit_file", "list_files", "search_files");
        sut.Tools.Select(t => JsonDocument.Parse(t.InputSchema).RootElement.GetProperty("type").GetString()).Should().AllBe("object");
    }

    [Fact]
    public void Limit_LongText_IsCutWithANote()
    {
        // Act
        var result = ToolSet.Limit(new string('a', ToolSet.MaxOutputCharacters + 25));

        // Assert
        result.Should().StartWith(new string('a', ToolSet.MaxOutputCharacters)).And.EndWith("[Output cut: 25 more characters were left out.]");
    }

    [Fact]
    public void Limit_CutInsideASurrogatePair_KeepsTheTextValid()
    {
        // Arrange
        var text = new string('a', ToolSet.MaxOutputCharacters - 1) + "😀tail";

        // Act
        var result = ToolSet.Limit(text);

        // Assert
        result.Should().NotContain("\uD83D");
    }

    [Fact]
    public void Limit_ShortText_IsUnchanged()
    {
        // Act
        var result = ToolSet.Limit("short");

        // Assert
        result.Should().Be("short");
    }

    [Fact]
    public async Task ExecuteAsync_BadArguments_BecomesAnErrorResult()
    {
        // Arrange
        var sut = new ToolSet([new ReadFileTool()], _workspace.Context);

        // Act
        var missing = await sut.ExecuteAsync(sut.Find("read_file")!, "{}", TestContext.Current.CancellationToken);
        var broken = await sut.ExecuteAsync(sut.Find("read_file")!, "{oops", TestContext.Current.CancellationToken);

        // Assert
        missing.Should().Be(ToolResult.Error("The 'path' argument is required."));
        broken.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_IoFailure_BecomesAnErrorResult()
    {
        // Arrange
        var sut = new ToolSet([new ThrowingTool(new IOException("disk on fire"))], _workspace.Context);

        // Act
        var result = await sut.ExecuteAsync(sut.Tools[0], "{}", TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be(ToolResult.Error("disk on fire"));
    }

    [Fact]
    public async Task ExecuteAsync_ToolRunsTooLong_IsStoppedWithATimeoutResult()
    {
        // Arrange
        var sut = new ToolSet([new HangingTool(TimeSpan.FromMilliseconds(50))], _workspace.Context);

        // Act
        var result = await sut.ExecuteAsync(sut.Tools[0], "{}", TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Contain("took longer");
    }

    [Fact]
    public async Task ExecuteAsync_CallerCancels_Throws()
    {
        // Arrange
        var sut = new ToolSet([new HangingTool(TimeSpan.FromMinutes(1))], _workspace.Context);
        var cancelled = new CancellationToken(canceled: true);

        // Act
        var act = () => sut.ExecuteAsync(sut.Tools[0], "{}", cancelled);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteAsync_HugeOutput_IsLimited()
    {
        // Arrange
        var sut = new ToolSet([new ThrowingTool(null, new string('x', ToolSet.MaxOutputCharacters * 2))], _workspace.Context);

        // Act
        var result = await sut.ExecuteAsync(sut.Tools[0], "{}", TestContext.Current.CancellationToken);

        // Assert
        result.Output.Length.Should().BeLessThan(ToolSet.MaxOutputCharacters + 100);
    }

    [Fact]
    public void Summarize_UnreadableArguments_IsEmpty()
    {
        // Act
        var result = ToolSet.Summarize(new ReadFileTool(), "{oops");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Summarize_PathArgument_IsThePath()
    {
        // Act
        var result = ToolSet.Summarize(new ReadFileTool(), """{"path":"src/a.cs"}""");

        // Assert
        result.Should().Be("src/a.cs");
    }

    private abstract class FakeTool : ITool
    {
        public string Name => "fake";

        public string Description => "A fake tool.";

        public string InputSchema => """{"type":"object"}""";

        public bool IsReadOnly => true;

        public virtual TimeSpan Timeout => TimeSpan.FromSeconds(5);

        public string Summarize(JsonElement arguments) => string.Empty;

        public abstract Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken);
    }

    private sealed class ThrowingTool(Exception? exception, string output = "") : FakeTool
    {
        public override Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken)
        {
            return exception is null ? Task.FromResult(ToolResult.Ok(output)) : throw exception;
        }
    }

    private sealed class HangingTool(TimeSpan timeout) : FakeTool
    {
        public override TimeSpan Timeout => timeout;

        public override async Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
            return ToolResult.Ok("never");
        }
    }
}
