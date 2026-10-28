using BlazeHarness.Core.Tools;
using BlazeHarness.Tests.Shared;
using Microsoft.Extensions.Time.Testing;
using System.Text.Json;

namespace BlazeHarness.Core.Tests.Unit.Tools;

public sealed class ToolSetTests
{
    private readonly ToolContext _context = new(Path.GetTempPath());

    [Fact]
    public void Constructor_DuplicateNames_Throws()
    {
        // Act
        var act = () => new ToolSet([new ReadFileTool(), new ReadFileTool()], _context);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*read_file*");
    }

    [Fact]
    public void With_ExtraTools_AreOfferedAfterTheExistingOnes()
    {
        // Arrange
        var sut = new ToolSet([new ReadFileTool()], _context);

        // Act
        var more = sut.With([Named("extra_one"), Named("extra_two")]);

        // Assert
        more.Tools.Select(t => t.Name).Should().Equal("read_file", "extra_one", "extra_two");
        more.Declarations.Should().HaveCount(3);
        more.Context.Should().BeSameAs(sut.Context);
        sut.Tools.Should().HaveCount(1);
    }

    [Fact]
    public void With_NameAlreadyTaken_KeepsTheExistingToolAndSkipsTheNewOne()
    {
        // Arrange
        var sut = new ToolSet([new ReadFileTool()], _context);

        // Act
        var more = sut.With([Named("read_file"), Named("fresh"), Named("fresh")]);

        // Assert
        more.Tools.Select(t => t.Name).Should().Equal("read_file", "fresh");
        more.Find("read_file").Should().BeOfType<ReadFileTool>();
    }

    [Fact]
    public void With_NothingNew_ReturnsTheSameSet()
    {
        // Arrange
        var sut = new ToolSet([new ReadFileTool()], _context);

        // Assert
        sut.With([]).Should().BeSameAs(sut);
    }

    [Fact]
    public void Declarations_BuiltInTools_HaveNamesAndSchemas()
    {
        // Act
        var sut = new ToolSet([new ReadFileTool(), new WriteFileTool(), new EditFileTool(), new ListFilesTool(), new SearchFilesTool()], _context);

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
        var sut = new ToolSet([new ReadFileTool()], _context);

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
        var sut = new ToolSet([Tool(TimeSpan.FromSeconds(5), _ => throw new IOException("disk on fire"))], _context);

        // Act
        var result = await sut.ExecuteAsync(sut.Tools[0], "{}", TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be(ToolResult.Error("disk on fire"));
    }

    [Fact]
    public async Task ExecuteAsync_ToolRunsTooLong_IsStoppedWithATimeoutResult()
    {
        // Arrange
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var time = new FakeTimeProvider();
        var sut = new ToolSet([Hanging(TimeSpan.FromSeconds(5), started)], _context, time);

        // Act
        var run = sut.ExecuteAsync(sut.Tools[0], "{}", TestContext.Current.CancellationToken);
        await started.Task.Bounded();
        time.Advance(TimeSpan.FromSeconds(6));
        var result = await run.Bounded();

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Contain("took longer");
    }

    [Fact]
    public async Task ExecuteAsync_CallerCancels_Throws()
    {
        // Arrange
        var sut = new ToolSet([Hanging(TimeSpan.FromMinutes(1), started: null)], _context);
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
        var sut = new ToolSet([Tool(TimeSpan.FromSeconds(5), _ => Task.FromResult(ToolResult.Ok(new string('x', ToolSet.MaxOutputCharacters * 2))))], _context);

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

    [Fact]
    public async Task ExecuteAsync_ToolBlockedInSynchronousIo_IsAbandonedWhenTheCallerCancels()
    {
        // Arrange
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new ManualResetEventSlim();
        var blocking = Tool(TimeSpan.FromMinutes(1), _ =>
        {
            started.SetResult();
            release.Wait(TestContext.Current.CancellationToken);
            return Task.FromResult(ToolResult.Ok("late"));
        });
        var sut = new ToolSet([blocking], _context);
        var cancel = new CancellationTokenSource();
        try
        {
            var call = sut.ExecuteAsync(sut.Tools[0], "{}", cancel.Token);
            await started.Task.WaitAsync(TestTimeouts.Default, TestContext.Current.CancellationToken);

            // Act
            await cancel.CancelAsync();
            var act = () => call.WaitAsync(TestTimeouts.Default, TestContext.Current.CancellationToken);

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            release.Set();
            release.Dispose();
            cancel.Dispose();
        }
    }

    private static ITool Tool(TimeSpan timeout, Func<CancellationToken, Task<ToolResult>> run)
    {
        var tool = Substitute.For<ITool>();
        tool.Name.Returns("fake");
        tool.Description.Returns("A fake tool.");
        tool.InputSchema.Returns("""{"type":"object"}""");
        tool.IsReadOnly.Returns(true);
        tool.Timeout.Returns(timeout);
        tool.Summarize(Arg.Any<JsonElement>()).Returns(string.Empty);
        tool.ExecuteAsync(Arg.Any<ToolArguments>(), Arg.Any<ToolContext>(), Arg.Any<CancellationToken>()).Returns(call => run(call.Arg<CancellationToken>()));
        return tool;
    }

    private static ITool Named(string name)
    {
        var tool = Substitute.For<ITool>();
        tool.Name.Returns(name);
        tool.Description.Returns("A fake tool.");
        tool.InputSchema.Returns("""{"type":"object"}""");
        return tool;
    }

    private static ITool Hanging(TimeSpan timeout, TaskCompletionSource? started)
    {
        return Tool(timeout, async token =>
        {
            started?.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return ToolResult.Ok("never");
        });
    }
}
