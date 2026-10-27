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

    public static bool IsWindows => OperatingSystem.IsWindows();

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

    [Fact(Skip = "Symlinks need elevated rights on Windows.", SkipWhen = nameof(IsWindows))]
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

    [Fact(Skip = "Symlinks need elevated rights on Windows.", SkipWhen = nameof(IsWindows))]
    public async Task ExecuteAsync_FolderLinkLoop_IsListedOnceAndDoesNotFail()
    {
        // Arrange
        Directory.CreateSymbolicLink(Path.Combine(_folder, "templates", "again"), _folder);

        // Act
        var result = await RunAsync(new { name = "report" });

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().Contain("- templates/weekly.md").And.NotContain("again/");
    }

    [Fact]
    public async Task ExecuteAsync_InstructionsGrewPastTheLimit_IsAnError()
    {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_folder, SkillInfo.FileName), new string('x', (int)SkillScanner.MaxFileBytes + 1), TestContext.Current.CancellationToken);

        // Act
        var result = await RunAsync(new { name = "report" });

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Contain("larger than 256 KB");
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
    public async Task ExecuteAsync_HiddenSkillNobodyStarted_IsRefusedAndNotNamed()
    {
        // Arrange
        var sut = ToolWith(started: new HashSet<string>());

        // Act
        var result = await RunAsync(sut, new { name = "manual" });

        // Assert
        result.Should().Be(ToolResult.Error("There is no skill named 'manual'. The skills are: guide, report."));
    }

    [Fact]
    public async Task ExecuteAsync_HiddenSkillTheUserStarted_Loads()
    {
        // Arrange
        var sut = ToolWith(started: new HashSet<string> { "manual" });

        // Act
        var result = await RunAsync(sut, new { name = "manual" });

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().Contain("Skill: manual");
    }

    [Fact]
    public async Task ExecuteAsync_HiddenSkillNamedByALoadableSkill_Loads()
    {
        // Arrange
        var sut = ToolWith(started: new HashSet<string>(), guideBody: "Finish by running manual.");

        // Act
        var result = await RunAsync(sut, new { name = "manual" });

        // Assert
        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_HiddenSkillOnlyNamedInsideAnotherWord_IsRefused()
    {
        // Arrange
        var sut = ToolWith(started: new HashSet<string>(), guideBody: "Use manual-labor and manuals.");

        // Act
        var result = await RunAsync(sut, new { name = "manual" });

        // Assert
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_HiddenSkillNamedAfterAWordThatContainsIt_Loads()
    {
        // Arrange
        var sut = ToolWith(started: new HashSet<string>(), guideBody: "Not manuals, but manual");

        // Act
        var result = await RunAsync(sut, new { name = "manual" });

        // Assert
        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task LoadableBy_ChainOfHiddenSkills_FollowsMentionsFromReachableOnesOnly()
    {
        // Arrange
        var chained = _folders.Add("skills", "chained", "Second hidden.", "Done.");
        var manual = _folders.Add("skills", "manual", "First hidden.", "Then run chained.");
        var guide = _folders.Add("skills", "guide", "Visible.", "Nothing here.");
        var skills = new SkillSet(
        [
            new SkillInfo("guide", "Visible.", guide, SkillSource.App),
            new SkillInfo("manual", "First hidden.", manual, SkillSource.App, ModelInvocable: false),
            new SkillInfo("chained", "Second hidden.", chained, SkillSource.App, ModelInvocable: false),
        ]);

        // Act
        var unreachable = skills.LoadableBy(new HashSet<string>());
        var reachable = skills.LoadableBy(new HashSet<string> { "manual" });

        // Assert
        unreachable.Should().BeEquivalentTo("guide");
        reachable.Should().BeEquivalentTo("guide", "manual", "chained");
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

    private LoadSkillTool ToolWith(IReadOnlySet<string> started, string guideBody = "Nothing here.")
    {
        var manual = _folders.Add("skills", "manual", "Only by hand.");
        var guide = _folders.Add("skills", "guide", "Visible.", guideBody);
        var skills = new SkillSet(
        [
            new SkillInfo("report", "Write reports.", _folder, SkillSource.App),
            new SkillInfo("guide", "Visible.", guide, SkillSource.App),
            new SkillInfo("manual", "Only by hand.", manual, SkillSource.App, ModelInvocable: false),
        ]);
        return new LoadSkillTool(skills, started);
    }

    private Task<ToolResult> RunAsync(LoadSkillTool tool, object args) => tool.ExecuteAsync(TempWorkspace.Args(args), _context, TestContext.Current.CancellationToken);

    private Task<ToolResult> RunAsync(object args) => _sut.ExecuteAsync(TempWorkspace.Args(args), _context, TestContext.Current.CancellationToken);
}
