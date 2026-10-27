using BlazeHarness.Core.Skills;
using BlazeHarness.Core.Tests.Integration.Tools;

namespace BlazeHarness.Core.Tests.Integration.Skills;

public sealed class SkillScannerTests : IDisposable
{
    private readonly SkillFolders _folders = new();

    public void Dispose() => _folders.Dispose();

    [Fact]
    public void Scan_MissingFolder_FindsNothing()
    {
        // Arrange
        var missing = Path.Combine(_folders.Root, "nope");

        // Act
        var scan = SkillScanner.Scan(missing, SkillSource.App);

        // Assert
        scan.Should().Be(SkillScan.Empty);
    }

    [Fact]
    public void Scan_SkillFolders_ReadsThemInNameOrder()
    {
        // Arrange
        _folders.Add("skills", "zeta", "Last one.");
        var alpha = _folders.Add("skills", "alpha", "First one.");
        Directory.CreateDirectory(Path.Combine(_folders.Root, "skills", "not-a-skill"));

        // Act
        var scan = SkillScanner.Scan(Path.Combine(_folders.Root, "skills"), SkillSource.App);

        // Assert
        scan.Skills.Select(s => s.Name).Should().Equal("alpha", "zeta");
        scan.Skills[0].Should().Be(new SkillInfo("alpha", "First one.", alpha, SkillSource.App));
        scan.Problems.Should().BeEmpty();
    }

    [Fact]
    public void Scan_DisableModelInvocation_MarksTheSkillAsNotModelInvocable()
    {
        // Arrange
        _folders.AddRaw("skills", "manual", "---\nname: manual\ndescription: Only by hand.\ndisable-model-invocation: true\n---\nbody");
        _folders.Add("skills", "auto", "Either way.");

        // Act
        var scan = SkillScanner.Scan(Path.Combine(_folders.Root, "skills"), SkillSource.App);

        // Assert
        scan.Skills.Should().ContainSingle(s => s.Name == "manual").Which.ModelInvocable.Should().BeFalse();
        scan.Skills.Should().ContainSingle(s => s.Name == "auto").Which.ModelInvocable.Should().BeTrue();
    }

    [Fact]
    public void Scan_NameMissing_UsesTheFolderName()
    {
        // Arrange
        _folders.AddRaw("skills", "from-folder", "---\ndescription: No name field.\n---\nbody");

        // Act
        var scan = SkillScanner.Scan(Path.Combine(_folders.Root, "skills"), SkillSource.App);

        // Assert
        scan.Skills.Should().ContainSingle().Which.Name.Should().Be("from-folder");
    }

    [Theory]
    [InlineData("Upper")]
    [InlineData("has space")]
    [InlineData("-leading")]
    [InlineData("double--hyphen")]
    public void Scan_InvalidName_IsSkippedWithAProblem(string name)
    {
        // Arrange
        _folders.AddRaw("skills", "folder", $"---\nname: \"{name}\"\ndescription: Fine.\n---\nbody");

        // Act
        var scan = SkillScanner.Scan(Path.Combine(_folders.Root, "skills"), SkillSource.App);

        // Assert
        scan.Skills.Should().BeEmpty();
        scan.Problems.Should().ContainSingle().Which.Should().StartWith("folder:").And.Contain("not a valid name");
    }

    [Fact]
    public void Scan_NameLongerThanTheLimit_IsSkipped()
    {
        // Arrange
        _folders.AddRaw("skills", "folder", $"---\nname: {new string('a', 65)}\ndescription: Fine.\n---\nbody");

        // Act
        var scan = SkillScanner.Scan(Path.Combine(_folders.Root, "skills"), SkillSource.App);

        // Assert
        scan.Skills.Should().BeEmpty();
        scan.Problems.Should().ContainSingle();
    }

    [Fact]
    public void Scan_NoDescription_IsSkippedWithAProblem()
    {
        // Arrange
        _folders.AddRaw("skills", "bare", "---\nname: bare\n---\nbody");

        // Act
        var scan = SkillScanner.Scan(Path.Combine(_folders.Root, "skills"), SkillSource.App);

        // Assert
        scan.Skills.Should().BeEmpty();
        scan.Problems.Should().ContainSingle().Which.Should().Be("bare: The front matter has no description.");
    }

    [Fact]
    public void Scan_LongDescription_IsCutToTheLimit()
    {
        // Arrange
        _folders.Add("skills", "wordy", new string('w', 3000));

        // Act
        var scan = SkillScanner.Scan(Path.Combine(_folders.Root, "skills"), SkillSource.App);

        // Assert
        scan.Skills.Should().ContainSingle().Which.Description.Should().HaveLength(SkillScanner.MaxDescriptionLength).And.EndWith("…");
    }

    [Fact]
    public void Scan_HugeFile_IsSkipped()
    {
        // Arrange
        _folders.AddRaw("skills", "huge", $"---\nname: huge\ndescription: Big.\n---\n{new string('x', (int)SkillScanner.MaxFileBytes)}");

        // Act
        var scan = SkillScanner.Scan(Path.Combine(_folders.Root, "skills"), SkillSource.App);

        // Assert
        scan.Skills.Should().BeEmpty();
        scan.Problems.Should().ContainSingle().Which.Should().Contain("larger than 256 KB");
    }

    [Fact]
    public void Scan_MoreThanTheLimit_ReadsTheFirstOnes()
    {
        // Arrange
        foreach (var i in Enumerable.Range(0, SkillScanner.MaxSkills + 5))
        {
            _folders.Add("skills", $"s{i:D4}", "Fine.");
        }

        // Act
        var scan = SkillScanner.Scan(Path.Combine(_folders.Root, "skills"), SkillSource.App);

        // Assert
        scan.Skills.Should().HaveCount(SkillScanner.MaxSkills);
        scan.Problems.Should().ContainSingle().Which.Should().Contain("only the first");
    }

    [Fact]
    public void Scan_ProjectLinkLeavingTheFolder_IsSkipped()
    {
        // Arrange
        var outside = _folders.Add("elsewhere", "sneaky", "Outside the project.");
        var project = Path.Combine(_folders.Root, "project");
        Directory.CreateDirectory(Path.Combine(project, ".agents", "skills"));
        Directory.CreateSymbolicLink(Path.Combine(project, ".agents", "skills", "sneaky"), outside);
        _folders.Add("project/.agents/skills", "honest", "Inside the project.");

        // Act
        var scan = SkillScanner.Scan(Path.Combine(project, ".agents", "skills"), SkillSource.Project, project);

        // Assert
        scan.Skills.Select(s => s.Name).Should().Equal("honest");
        scan.Problems.Should().ContainSingle().Which.Should().Contain("links outside");
    }

    [Fact]
    public void Scan_LinkInsideTheProject_IsKept()
    {
        // Arrange
        var inside = _folders.Add("project/shared", "reused", "Shared inside the project.");
        var skills = Path.Combine(_folders.Root, "project", ".agents", "skills");
        Directory.CreateDirectory(skills);
        Directory.CreateSymbolicLink(Path.Combine(skills, "reused"), inside);

        // Act
        var scan = SkillScanner.Scan(skills, SkillSource.Project, Path.Combine(_folders.Root, "project"));

        // Assert
        scan.Skills.Should().ContainSingle().Which.Name.Should().Be("reused");
    }

    [Fact]
    public void Scan_LinkWithoutABoundary_IsFollowed()
    {
        // Arrange
        var outside = _folders.Add("elsewhere", "shared", "Linked from the user's folder.");
        var skills = Path.Combine(_folders.Root, "skills");
        Directory.CreateDirectory(skills);
        Directory.CreateSymbolicLink(Path.Combine(skills, "shared"), outside);

        // Act
        var scan = SkillScanner.Scan(skills, SkillSource.UserAgents);

        // Assert
        scan.Skills.Should().ContainSingle().Which.Name.Should().Be("shared");
    }

    [Fact]
    public void Scan_ProjectSkillsFolderIsALinkOutside_IsSkipped()
    {
        // Arrange
        _folders.Add("elsewhere", "sneaky", "Outside the project.");
        var project = Path.Combine(_folders.Root, "project");
        Directory.CreateDirectory(Path.Combine(project, ".agents"));
        Directory.CreateSymbolicLink(Path.Combine(project, ".agents", "skills"), Path.Combine(_folders.Root, "elsewhere"));

        // Act
        var scan = SkillScanner.Scan(Path.Combine(project, ".agents", "skills"), SkillSource.Project, project);

        // Assert
        scan.Skills.Should().BeEmpty();
        scan.Problems.Should().ContainSingle().Which.Should().Contain("links outside");
    }

    [Fact]
    public void Scan_ProjectAgentsFolderIsALinkOutside_IsSkipped()
    {
        // Arrange
        _folders.Add("elsewhere/skills", "sneaky", "Outside the project.");
        var project = Path.Combine(_folders.Root, "project");
        Directory.CreateDirectory(project);
        Directory.CreateSymbolicLink(Path.Combine(project, ".agents"), Path.Combine(_folders.Root, "elsewhere"));

        // Act
        var scan = SkillScanner.Scan(Path.Combine(project, ".agents", "skills"), SkillSource.Project, project);

        // Assert
        scan.Skills.Should().BeEmpty();
        scan.Problems.Should().ContainSingle().Which.Should().Contain("links outside");
    }

    [Fact(Skip = "Named pipes are a Unix feature.", SkipUnless = nameof(TempWorkspace.CanMakeFifo), SkipType = typeof(TempWorkspace))]
    public void Scan_SkillFileIsANamedPipe_IsSkippedWithoutReadingIt()
    {
        // Arrange
        var folder = Path.Combine(_folders.Root, "skills", "pipe");
        Directory.CreateDirectory(folder);
        using (var mkfifo = System.Diagnostics.Process.Start("mkfifo", Path.Combine(folder, SkillInfo.FileName)))
        {
            mkfifo.WaitForExit();
        }

        // Act
        var scan = SkillScanner.Scan(Path.Combine(_folders.Root, "skills"), SkillSource.App);

        // Assert
        scan.Skills.Should().BeEmpty();
        scan.Problems.Should().ContainSingle().Which.Should().Contain("empty");
    }
}
