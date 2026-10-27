using BlazeHarness.Core.Skills;
using BlazeHarness.Core.Tests.Integration.Tools;
using BlazeHarness.Core.Tools;
using System.Text.Json;

namespace BlazeHarness.Core.Tests.Integration.Skills;

public sealed class LoadSkillToolTests : IDisposable
{
    private readonly SkillFolders _folders = new();
    private readonly ToolContext _context;
    private readonly string _folder;
    private readonly LoadSkillTool _sut;

    public LoadSkillToolTests()
    {
        _folder = _folders.Add("skills", "report", "Write reports.", "Step one.\nStep two.");
        Directory.CreateDirectory(Path.Combine(_folder, "templates"));
        File.WriteAllText(Path.Combine(_folder, "templates", "weekly.md"), "line1\nline2\nline3");
        File.WriteAllBytes(Path.Combine(_folder, "logo.png"), [0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 1, 2, 3]);
        File.WriteAllText(Path.Combine(_folders.Root, "secret.txt"), "TOPSECRET");
        _context = new ToolContext(_folders.Root);
        _sut = new LoadSkillTool(new SkillSet([new SkillInfo("report", "Write reports.", _folder, SkillSource.App)]));
    }

    public void Dispose() => _folders.Dispose();

    [Fact]
    public async Task ExecuteAsync_NameOnly_ReturnsTheFolderFilesAndInstructionsWithoutFrontMatter()
    {
        // Arrange

        // Act
        var result = await RunAsync(new { name = "report" });

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().Contain("Skill: report").And.Contain($"Folder: {_folder}")
            .And.Contain("- logo.png").And.Contain("- templates/weekly.md")
            .And.Contain("Step one.\nStep two.".ReplaceLineEndings())
            .And.NotContain("description:").And.NotContain("SKILL.md");
    }

    [Fact]
    public async Task ExecuteAsync_UnknownName_ListsTheKnownOnes()
    {
        // Arrange

        // Act
        var result = await RunAsync(new { name = "nope" });

        // Assert
        result.Should().Be(ToolResult.Error("There is no skill named 'nope'. The skills are: report."));
    }

    [Fact]
    public async Task ExecuteAsync_WithPath_ReadsABundledFile()
    {
        // Arrange

        // Act
        var result = await RunAsync(new { name = "report", path = "templates/weekly.md", offset = 2 });

        // Assert
        result.Output.Should().StartWith("line2\nline3");
    }

    [Theory]
    [InlineData("../../secret.txt")]
    [InlineData("../report/../../secret.txt")]
    public async Task ExecuteAsync_PathOutsideTheSkillFolder_IsAnError(string path)
    {
        // Arrange

        // Act
        var result = await RunAsync(new { name = "report", path });

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().NotContain("TOPSECRET");
    }

    [Fact]
    public async Task ExecuteAsync_PathThroughALinkOutside_IsAnError()
    {
        // Arrange
        Directory.CreateSymbolicLink(Path.Combine(_folder, "escape"), _folders.Root);

        // Act
        var result = await RunAsync(new { name = "report", path = "escape/secret.txt" });

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().NotContain("TOPSECRET");
    }

    [Fact]
    public async Task ExecuteAsync_BinaryFileOrMissingFile_IsAnError()
    {
        // Arrange

        // Act
        var binary = await RunAsync(new { name = "report", path = "logo.png" });
        var missing = await RunAsync(new { name = "report", path = "nothing.md" });

        // Assert
        binary.Output.Should().Contain("binary");
        missing.Should().Be(ToolResult.Error("The skill 'report' has no file 'nothing.md'."));
    }

    [Fact]
    public async Task ExecuteAsync_HugeInstructions_AreCutOff()
    {
        // Arrange
        var huge = _folders.Add("skills", "huge", "Big.", new string('x', 100_000));
        var sut = new LoadSkillTool(new SkillSet([new SkillInfo("huge", "Big.", huge, SkillSource.App)]));

        // Act
        var result = await sut.ExecuteAsync(TempWorkspace.Args(new { name = "huge" }), _context, TestContext.Current.CancellationToken);

        // Assert
        result.Output.Should().Contain("cut off after 65,536 characters");
        result.Output.Length.Should().BeLessThan(66_500);
    }

    [Fact]
    public void Summarize_WithAndWithoutPath_ShowsTheTarget()
    {
        // Arrange
        using var plain = JsonDocument.Parse("""{"name":"report"}""");
        using var file = JsonDocument.Parse("""{"name":"report","path":"a.md"}""");

        // Act
        var summaries = new[] { _sut.Summarize(plain.RootElement), _sut.Summarize(file.RootElement) };

        // Assert
        summaries.Should().Equal("report", "report/a.md");
    }

    private Task<ToolResult> RunAsync(object args) => _sut.ExecuteAsync(TempWorkspace.Args(args), _context, TestContext.Current.CancellationToken);
}
