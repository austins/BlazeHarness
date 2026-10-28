using BlazeHarness.Core.Config;
using BlazeHarness.Core.Sessions;
using BlazeHarness.Core.Skills;
using BlazeHarness.Tests.Shared;
using BlazeHarness.Tests.Shared.Agent;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable IDISP001 // The service disposes the sessions it opens.

namespace BlazeHarness.Core.Tests.Integration.Sessions;

public sealed class SessionServiceSkillsTests : IDisposable
{
    private readonly SessionServiceRig _rig = new("bh-svc-skills-", "ok");
    private readonly JsonFileStore<AppConfig> _config;
    private readonly SkillCatalog _skills;

    public SessionServiceSkillsTests()
    {
        WriteSkill(Path.Combine(_rig.Paths.SkillsDirectory, "report"), "report", "Write reports.");
        WriteSkill(Path.Combine(_rig.Work, ".agents", "skills", "local"), "local", "Project only.");
        _config = new JsonFileStore<AppConfig>(_rig.Paths.ConfigFile, NullLogger<JsonFileStore<AppConfig>>.Instance, watch: false);
        _skills = new SkillCatalog(_rig.Paths, _config, new LoggerFake<SkillCatalog>(), watch: false);
        _rig.Build(skills: _skills);
    }

    private SessionService Sut => _rig.Service;

    public void Dispose()
    {
        _rig.Dispose();
        _skills.Dispose();
        _config.Dispose();
    }

    [Fact]
    public async Task SendAsync_GlobalAndProjectSkills_AreListedAndTheToolIsOffered()
    {
        // Arrange
        var info = await Sut.CreateAsync(_rig.Work, TestContext.Current.CancellationToken);

        // Act
        await Sut.SendAsync(info.Id, "hi", TestTargets.Create("p", "m"), cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var prompt = _rig.Client.Requests[0][0].Text;
        prompt.Should().Contain("- local: Project only.").And.Contain("- report: Write reports.");
        _rig.Client.Options[0]!.Tools!.Select(t => t.Name).Should().Contain("load_skill");
    }

    [Fact]
    public async Task SendAsync_SkillTurnedOffForTheSession_IsNotListed()
    {
        // Arrange
        var info = await Sut.CreateAsync(_rig.Work, TestContext.Current.CancellationToken);
        await Sut.SetSkillAsync(info.Id, "report", enabled: false, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await Sut.SendAsync(info.Id, "hi", TestTargets.Create("p", "m"), cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        _rig.Client.Requests[0][0].Text.Should().Contain("- local:").And.NotContain("- report:");
    }

    [Fact]
    public async Task SendAsync_SkillOffGloballyButOnForTheSession_IsListed()
    {
        // Arrange
        await _skills.SetDisabledAsync("report", true, TestContext.Current.CancellationToken);
        var info = await Sut.CreateAsync(_rig.Work, TestContext.Current.CancellationToken);
        await Sut.SetSkillAsync(info.Id, "report", enabled: true, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await Sut.SendAsync(info.Id, "hi", TestTargets.Create("p", "m"), cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        _rig.Client.Requests[0][0].Text.Should().Contain("- report:");
    }

    [Fact]
    public async Task SendAsync_AllSkillsOff_AddsNothingToThePrompt()
    {
        // Arrange
        var info = await Sut.CreateAsync(_rig.Work, TestContext.Current.CancellationToken);
        await Sut.SetSkillAsync(info.Id, "report", enabled: false, cancellationToken: TestContext.Current.CancellationToken);
        await Sut.SetSkillAsync(info.Id, "local", enabled: false, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await Sut.SendAsync(info.Id, "hi", TestTargets.Create("p", "m"), cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        _rig.Client.Requests[0][0].Text.Should().NotContain("load_skill");
        _rig.Client.Options[0]!.Tools!.Select(t => t.Name).Should().NotContain("load_skill");
    }

    [Fact]
    public async Task SetSkillAsync_ChoiceThenNull_SavesAndClearsIt()
    {
        // Arrange
        var info = await Sut.CreateAsync(_rig.Work, TestContext.Current.CancellationToken);

        // Act
        await Sut.SetSkillAsync(info.Id, "report", enabled: false, cancellationToken: TestContext.Current.CancellationToken);
        var enabledAfterChoice = Sut.IsSkillEnabled(info.Id, "report");
        var savedAfterChoice = _rig.Store.Find(info.Id)!.Skills;
        await Sut.SetSkillAsync(info.Id, "report", enabled: null, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        enabledAfterChoice.Should().BeFalse();
        savedAfterChoice.Should().ContainKey("report").WhoseValue.Should().BeFalse();
        Sut.IsSkillEnabled(info.Id, "report").Should().BeTrue();
        _rig.Store.Find(info.Id)!.Skills.Should().BeNull();
    }

    private static void WriteSkill(string folder, string name, string description)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, SkillInfo.FileName), $"---\nname: {name}\ndescription: {description}\n---\nBody.\n");
    }
}
