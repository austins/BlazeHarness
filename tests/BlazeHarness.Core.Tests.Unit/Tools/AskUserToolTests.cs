using BlazeHarness.Core.Tools;
using System.Text.Json;

namespace BlazeHarness.Core.Tests.Unit.Tools;

public sealed class AskUserToolTests
{
    private readonly AskUserTool _sut = new();

    [Fact]
    public void Parse_QuestionWithOptions_ReadsLabelsDescriptionsAndMultiSelect()
    {
        // Arrange
        var arguments = ToolArguments.Parse("""{"question":" Which one? ","options":[{"label":"A","description":"first"},{"label":"B"}],"multi_select":true}""");

        // Act
        var question = AskUserTool.Parse(arguments);

        // Assert
        question.Question.Should().Be("Which one?");
        question.Options.Select(o => (o.Label, o.Description)).Should().Equal(("A", "first"), ("B", null));
        question.MultiSelect.Should().BeTrue();
    }

    [Fact]
    public void Parse_NoOptions_IsAnOpenQuestion()
    {
        // Act
        var question = AskUserTool.Parse(ToolArguments.Parse("""{"question":"Why?","multi_select":true}"""));

        // Assert
        question.Options.Should().BeEmpty();
        question.MultiSelect.Should().BeFalse();
    }

    [Fact]
    public void Parse_OptionsAsPlainStrings_AreAccepted()
    {
        // Act
        var question = AskUserTool.Parse(ToolArguments.Parse("""{"question":"Which?","options":["A","B"],"multi_select":"false"}"""));

        // Assert
        question.Options.Select(o => o.Label).Should().Equal("A", "B");
        question.MultiSelect.Should().BeFalse();
    }

    [Fact]
    public void Parse_OptionsWrittenAsAJsonString_AreAccepted()
    {
        // Act
        var question = AskUserTool.Parse(ToolArguments.Parse("""{"question":"Which?","options":"[{\"label\":\"A\"},{\"label\":\"B\"}]"}"""));

        // Assert
        question.Options.Select(o => o.Label).Should().Equal("A", "B");
    }

    [Theory]
    [InlineData("""{"question":""}""")]
    [InlineData("""{"question":"   "}""")]
    [InlineData("""{"options":["A","B"]}""")]
    [InlineData("""{"question":"Which?","options":["A"]}""")]
    [InlineData("""{"question":"Which?","options":["A","a"]}""")]
    [InlineData("""{"question":"Which?","options":["A","B","C","D","E","F","G","H","I"]}""")]
    [InlineData("""{"question":"Which?","options":[{"label":"A"},{"description":"no label"}]}""")]
    public void Parse_InvalidArguments_ThrowsAnArgumentException(string json)
    {
        // Act
        var act = () => AskUserTool.Parse(ToolArguments.Parse(json));

        // Assert
        act.Should().Throw<ToolArgumentException>();
    }

    [Fact]
    public void Parse_EightOptions_IsAllowed()
    {
        // Arrange
        var options = string.Join(',', Enumerable.Range(1, 8).Select(i => $"\"Option {i}\""));

        // Act
        var question = AskUserTool.Parse(ToolArguments.Parse($$"""{"question":"Which?","options":[{{options}}]}"""));

        // Assert
        question.Options.Should().HaveCount(8);
    }

    [Fact]
    public void Parse_LongQuestionOrLabel_ThrowsAnArgumentException()
    {
        // Arrange
        var longQuestion = new string('q', 2001);
        var longLabel = new string('l', 101);

        // Act
        var question = () => AskUserTool.Parse(ToolArguments.Parse($$"""{"question":"{{longQuestion}}"}"""));
        var label = () => AskUserTool.Parse(ToolArguments.Parse($$"""{"question":"Which?","options":["A","{{longLabel}}"]}"""));

        // Assert
        question.Should().Throw<ToolArgumentException>();
        label.Should().Throw<ToolArgumentException>();
    }

    [Fact]
    public void TryParse_InvalidArguments_ReturnsNull()
    {
        // Act
        var question = AskUserTool.TryParse("""{"question":"Which?","options":["A"]}""");

        // Assert
        question.Should().BeNull();
    }

    [Fact]
    public void Summarize_LongMultiLineQuestion_IsOneLineCutAtAHundredCharacters()
    {
        // Arrange
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { question = "first\nsecond " + new string('x', 200) }));

        // Act
        var summary = _sut.Summarize(document.RootElement);

        // Assert
        summary.Should().StartWith("first second ").And.EndWith("…").And.NotContain("\n");
        summary.Length.Should().Be(101);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCalledDirectly_ReturnsAnError()
    {
        // Act
        var result = await _sut.ExecuteAsync(ToolArguments.Parse("""{"question":"Why?"}"""), new ToolContext(Path.GetTempPath()), TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public void Definition_IsReadOnlyWithoutATimeout()
    {
        // Assert
        _sut.IsReadOnly.Should().BeTrue();
        _sut.Timeout.Should().Be(Timeout.InfiniteTimeSpan);
        _sut.Name.Should().Be("ask_user");
        _sut.InputSchema.Should().Contain("multi_select");
    }
}
