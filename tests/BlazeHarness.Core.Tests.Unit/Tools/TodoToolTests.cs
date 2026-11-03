using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Tools;
using System.Text.Json;

namespace BlazeHarness.Core.Tests.Unit.Tools;

public sealed class TodoToolTests
{
    private readonly TodoTool _sut = new();
    private readonly ToolContext _context = new(Path.GetTempPath());

    [Fact]
    public async Task ExecuteAsync_ValidList_ReportsTheProgress()
    {
        // Arrange
        var arguments = ToolArguments.Parse("""{"items":[{"text":"a","status":"completed"},{"text":"b","status":"in_progress"},{"text":"c","status":"pending"}]}""");

        // Act
        var result = await _sut.ExecuteAsync(arguments, _context, TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().StartWith(TodoList.UpdatedPrefix).And.Contain("1 of 3 done");
    }

    [Fact]
    public async Task ExecuteAsync_EmptyList_ClearsTheList()
    {
        // Arrange
        var arguments = ToolArguments.Parse("""{"items":[]}""");

        // Act
        var result = await _sut.ExecuteAsync(arguments, _context, TestContext.Current.CancellationToken);

        // Assert
        result.Output.Should().Contain("0 of 0 done");
    }

    [Fact]
    public async Task ExecuteAsync_MaximumItemsAndTextLength_Succeeds()
    {
        // Arrange
        var item = $$"""{"text":"{{new string('x', TodoList.MaxTextLength)}}","status":"pending"}""";
        var arguments = ToolArguments.Parse($$"""{"items":[{{string.Join(',', Enumerable.Repeat(item, TodoList.MaxItems))}}]}""");

        // Act
        var result = await _sut.ExecuteAsync(arguments, _context, TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeFalse();
    }

    [Theory]
    [InlineData("{}", "*'items'*")]
    [InlineData("""{"items":"a"}""", "*'items'*array*")]
    [InlineData("""{"items":["a"]}""", "*item 1*object*")]
    [InlineData("""{"items":[{"status":"pending"}]}""", "*item 1*'text'*")]
    [InlineData("""{"items":[{"text":"  ","status":"pending"}]}""", "*Item 1*empty text*")]
    [InlineData("""{"items":[{"text":"a\nb","status":"pending"}]}""", "*item 1*one line*")]
    [InlineData("""{"items":[{"text":"a","status":"done"}]}""", "*item 1*'status'*pending*in_progress*completed*")]
    [InlineData("""{"items":[{"text":"a"}]}""", "*item 1*'status'*")]
    public async Task ExecuteAsync_BadInput_ThrowsAClearError(string json, string message)
    {
        // Arrange
        var arguments = ToolArguments.Parse(json);

        // Act
        var act = () => _sut.ExecuteAsync(arguments, _context, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<ToolArgumentException>().WithMessage(message);
    }

    [Fact]
    public async Task ExecuteAsync_TextOverTheLimit_Throws()
    {
        // Arrange
        var arguments = ToolArguments.Parse($$"""{"items":[{"text":"{{new string('x', TodoList.MaxTextLength + 1)}}","status":"pending"}]}""");

        // Act
        var act = () => _sut.ExecuteAsync(arguments, _context, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<ToolArgumentException>().WithMessage($"*item 1*{TodoList.MaxTextLength}*");
    }

    [Fact]
    public async Task ExecuteAsync_TooManyItems_Throws()
    {
        // Arrange
        var item = """{"text":"a","status":"pending"}""";
        var arguments = ToolArguments.Parse($$"""{"items":[{{string.Join(',', Enumerable.Repeat(item, TodoList.MaxItems + 1))}}]}""");

        // Act
        var act = () => _sut.ExecuteAsync(arguments, _context, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<ToolArgumentException>().WithMessage($"*{TodoList.MaxItems}*");
    }

    [Fact]
    public async Task ExecuteAsync_BadInputThroughAToolSet_BecomesAnErrorResult()
    {
        // Arrange
        var tools = new ToolSet([_sut], _context, TimeProvider.System);

        // Act
        var result = await tools.ExecuteAsync(_sut, """{"items":[{"text":"a","status":"nope"}]}""", TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Contain("status");
    }

    [Fact]
    public void Summarize_ValidList_ShowsTheProgress()
    {
        // Arrange
        using var document = JsonDocument.Parse("""{"items":[{"text":"a","status":"completed"},{"text":"b","status":"pending"}]}""");

        // Act
        var summary = _sut.Summarize(document.RootElement);

        // Assert
        summary.Should().Be("1 of 2 done");
    }

    [Fact]
    public void Summarize_BadList_IsEmpty()
    {
        // Arrange
        using var document = JsonDocument.Parse("{}");

        // Act
        var summary = _sut.Summarize(document.RootElement);

        // Assert
        summary.Should().BeEmpty();
    }

    [Fact]
    public void Description_TellsTheModelWhenToUseIt()
    {
        // Arrange & Act
        var description = _sut.Description;

        // Assert
        description.Should().Contain("exactly one").And.Contain("not for");
    }

    [Fact]
    public void IsReadOnly_AndNoPathArguments_SoItNeedsNoApproval()
    {
        // Arrange & Act & Assert
        _sut.IsReadOnly.Should().BeTrue();
        ((ITool)_sut).PathArguments.Should().BeEmpty();
    }
}
