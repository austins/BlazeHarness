using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Config;
using BlazeHarness.Core.Providers;
using BlazeHarness.Core.Sessions;
using BlazeHarness.Core.Skills;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable IDISP001 // The service disposes the sessions it opens.

namespace BlazeHarness.Core.Tests.Integration.Sessions;

public sealed class SessionServiceSkillsTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "bh-svc-skills-" + Guid.NewGuid().ToString("N"));
    private readonly string _work;
    private readonly SessionStore _store;
    private readonly JsonFileStore<AppConfig> _config;
    private readonly SkillCatalog _skills;
    private readonly ScriptedChatClient _client = new([ScriptedChatClient.Text("ok")]);
    private readonly SessionService _sut;

    public SessionServiceSkillsTests()
    {
        _work = Path.Combine(_home, "work");
        Directory.CreateDirectory(_work);
        var paths = new AppPaths(Path.Combine(_home, "app"), _home);
        WriteSkill(Path.Combine(paths.SkillsDirectory, "report"), "report", "Write reports.");
        WriteSkill(Path.Combine(_work, ".agents", "skills", "local"), "local", "Project only.");
        _config = new JsonFileStore<AppConfig>(paths.ConfigFile, new LoggerFake<SessionServiceSkillsTests>(), watch: false);
        _skills = new SkillCatalog(paths, _config, new LoggerFake<SkillCatalog>(), watch: false);
        _store = new SessionStore(paths, NullLoggerFactory.Instance);
        _sut = new SessionService(_store, (_, _) => Task.FromResult<IChatClient>(_client), NullLoggerFactory.Instance, skills: _skills);
    }

    public void Dispose()
    {
        _sut.Dispose();
        _skills.Dispose();
        _config.Dispose();
        _store.Dispose();
        _client.Dispose();
        Directory.Delete(_home, recursive: true);
    }

    [Fact]
    public async Task SendAsync_GlobalAndProjectSkills_AreListedAndTheToolIsOffered()
    {
        // Arrange
        var info = await _sut.CreateAsync(_work, TestContext.Current.CancellationToken);

        // Act
        await _sut.SendAsync(info.Id, "hi", Target());

        // Assert
        var prompt = _client.Requests[0][0].Text;
        prompt.Should().Contain("- local: Project only.").And.Contain("- report: Write reports.");
        _client.Options[0]!.Tools!.Select(t => t.Name).Should().Contain("load_skill");
    }

    [Fact]
    public async Task SendAsync_SkillTurnedOffForTheSession_IsNotListed()
    {
        // Arrange
        var info = await _sut.CreateAsync(_work, TestContext.Current.CancellationToken);
        await _sut.SetSkillAsync(info.Id, "report", enabled: false);

        // Act
        await _sut.SendAsync(info.Id, "hi", Target());

        // Assert
        _client.Requests[0][0].Text.Should().Contain("- local:").And.NotContain("- report:");
    }

    [Fact]
    public async Task SendAsync_SkillOffGloballyButOnForTheSession_IsListed()
    {
        // Arrange
        await _skills.SetDisabledAsync("report", true, TestContext.Current.CancellationToken);
        var info = await _sut.CreateAsync(_work, TestContext.Current.CancellationToken);
        await _sut.SetSkillAsync(info.Id, "report", enabled: true);

        // Act
        await _sut.SendAsync(info.Id, "hi", Target());

        // Assert
        _client.Requests[0][0].Text.Should().Contain("- report:");
    }

    [Fact]
    public async Task SendAsync_AllSkillsOff_AddsNothingToThePrompt()
    {
        // Arrange
        var info = await _sut.CreateAsync(_work, TestContext.Current.CancellationToken);
        await _sut.SetSkillAsync(info.Id, "report", enabled: false);
        await _sut.SetSkillAsync(info.Id, "local", enabled: false);

        // Act
        await _sut.SendAsync(info.Id, "hi", Target());

        // Assert
        _client.Requests[0][0].Text.Should().NotContain("load_skill");
        _client.Options[0]!.Tools!.Select(t => t.Name).Should().NotContain("load_skill");
    }

    [Fact]
    public async Task SetSkillAsync_ChoiceThenNull_SavesAndClearsIt()
    {
        // Arrange
        var info = await _sut.CreateAsync(_work, TestContext.Current.CancellationToken);

        // Act
        await _sut.SetSkillAsync(info.Id, "report", enabled: false);
        var enabledAfterChoice = _sut.IsSkillEnabled(info.Id, "report");
        var savedAfterChoice = _store.Find(info.Id)!.Skills;
        await _sut.SetSkillAsync(info.Id, "report", enabled: null);

        // Assert
        enabledAfterChoice.Should().BeFalse();
        savedAfterChoice.Should().ContainKey("report").WhoseValue.Should().BeFalse();
        _sut.IsSkillEnabled(info.Id, "report").Should().BeTrue();
        _store.Find(info.Id)!.Skills.Should().BeNull();
    }

    private static void WriteSkill(string folder, string name, string description)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, SkillInfo.FileName), $"---\nname: {name}\ndescription: {description}\n---\nBody.\n");
    }

    private static ChatTarget Target()
    {
        var model = new ModelConfig { Id = "m", ContextWindow = 8000, MaxOutputTokens = 1000 };
        var provider = new ProviderConfig { Id = "p", DisplayName = "P", BaseUrl = "http://localhost/v1", Models = [model] };
        return new ChatTarget(provider, model, null);
    }
}
