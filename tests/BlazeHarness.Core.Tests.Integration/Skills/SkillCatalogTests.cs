using BlazeHarness.Core.Config;
using BlazeHarness.Core.Providers;
using BlazeHarness.Core.Skills;
using Microsoft.Extensions.Time.Testing;

namespace BlazeHarness.Core.Tests.Integration.Skills;

public sealed class SkillCatalogTests : IDisposable
{
    private readonly SkillFolders _folders = new();
    private readonly FakeTimeProvider _time = new();
    private readonly AppPaths _paths;
    private readonly JsonFileStore<AppConfig> _config;
    private readonly SkillCatalog _sut;

    public SkillCatalogTests()
    {
        _paths = new AppPaths(Path.Combine(_folders.Root, "home"), Path.Combine(_folders.Root, "user"));
        _config = new JsonFileStore<AppConfig>(_paths.ConfigFile, new LoggerFake<SkillCatalogTests>(), watch: false);
        _sut = new SkillCatalog(_paths, _config, new LoggerFake<SkillCatalog>(), watch: false, _time);
    }

    public void Dispose()
    {
        _sut.Dispose();
        _config.Dispose();
        _folders.Dispose();
    }

    [Fact]
    public void Resolve_SkillsInEveryFolder_AppliesPrecedence()
    {
        // Arrange
        _folders.Add("user/.agents/skills", "review", "From the user folder.");
        _folders.Add("home/skills", "review", "From the app folder.");
        _folders.Add("user/.agents/skills", "only-user", "Just the user.");
        var project = Path.Combine(_folders.Root, "project");
        _folders.Add("project/.agents/skills", "review", "From the project.");
        _sut.Refresh();

        // Act
        var result = _sut.Resolve(project);

        // Assert
        result.Skills.Select(s => (s.Name, s.Description)).Should().Equal(("only-user", "Just the user."), ("review", "From the project."));
        result.Shadowed.Should().HaveCount(2);
    }

    [Fact]
#pragma warning disable IDISP016, IDISP017 // Using the catalog after disposal is the point of the test.
    public void Refresh_AfterDispose_DoesNothing()
    {
        // Arrange
        var sut = new SkillCatalog(_paths, _config, new LoggerFake<SkillCatalog>(), watch: false);
        var raised = 0;
        sut.Changed += (_, _) => raised++;
        sut.Dispose();

        // Act
        sut.Refresh();

        // Assert
        raised.Should().Be(0);
    }
#pragma warning restore IDISP016, IDISP017

    [Fact]
    public void Resolve_NoFolder_UsesTheGlobalSkillsOnly()
    {
        // Arrange
        _folders.Add("home/skills", "global", "Everywhere.");
        _sut.Refresh();

        // Act
        var result = _sut.Resolve(null);

        // Assert
        result.Skills.Should().ContainSingle().Which.Name.Should().Be("global");
    }

    [Fact]
    public void Resolve_ProjectSkillAddedLater_IsFoundAfterTheCacheExpires()
    {
        // Arrange
        var project = Path.Combine(_folders.Root, "project");
        Directory.CreateDirectory(project);
        _sut.Resolve(project).Skills.Should().BeEmpty();
        _folders.Add("project/.agents/skills", "late", "Added after the first look.");
        _time.Advance(TimeSpan.FromSeconds(6));

        // Act
        var result = _sut.Resolve(project);

        // Assert
        result.Skills.Should().ContainSingle().Which.Name.Should().Be("late");
    }

    [Fact]
    public void Resolve_ProjectAskedAgainSoon_ReusesTheEarlierScan()
    {
        // Arrange
        var project = Path.Combine(_folders.Root, "project");
        Directory.CreateDirectory(project);
        _sut.Resolve(project);
        _folders.Add("project/.agents/skills", "late", "Added after the first look.");
        _time.Advance(TimeSpan.FromSeconds(1));

        // Act
        var result = _sut.Resolve(project);

        // Assert
        result.Skills.Should().BeEmpty();
    }

    [Fact]
    public void Refresh_ProjectScanCached_IsDropped()
    {
        // Arrange
        var project = Path.Combine(_folders.Root, "project");
        Directory.CreateDirectory(project);
        _sut.Resolve(project);
        _folders.Add("project/.agents/skills", "late", "Added after the first look.");

        // Act
        _sut.Refresh();

        // Assert
        _sut.Resolve(project).Skills.Should().ContainSingle();
    }

    [Fact]
    public void Refresh_GlobalSkillAdded_IsFoundAndRaisesChanged()
    {
        // Arrange
        var raised = 0;
        _sut.Changed += (_, _) => raised++;
        _folders.Add("home/skills", "fresh", "New.");

        // Act
        _sut.Refresh();

        // Assert
        raised.Should().Be(1);
        _sut.Resolve(null).Skills.Should().ContainSingle();
    }

    [Fact]
    public async Task SetDisabledAsync_Disable_RemovesTheSkillFromNewSetsAndPersists()
    {
        // Arrange
        _folders.Add("home/skills", "noisy", "Too chatty.");
        _folders.Add("home/skills", "useful", "Handy.");
        _sut.Refresh();

        // Act
        await _sut.SetDisabledAsync("noisy", true, TestContext.Current.CancellationToken);

        // Assert
        _sut.IsDisabledGlobally("noisy").Should().BeTrue();
        _sut.For(null, null).Skills.Select(s => s.Name).Should().Equal("useful");
        _config.Current.DisabledSkills.Should().Equal("noisy");
    }

    [Fact]
    public async Task SetDisabledAsync_EnableAgain_RemovesItFromTheList()
    {
        // Arrange
        await _sut.SetDisabledAsync("noisy", true, TestContext.Current.CancellationToken);

        // Act
        await _sut.SetDisabledAsync("noisy", false, TestContext.Current.CancellationToken);

        // Assert
        _sut.IsDisabledGlobally("noisy").Should().BeFalse();
    }

    [Fact]
    public async Task For_SessionOverrides_BeatTheGlobalChoice()
    {
        // Arrange
        _folders.Add("home/skills", "off-globally", "Disabled for everyone.");
        _folders.Add("home/skills", "on-globally", "Enabled for everyone.");
        _sut.Refresh();
        await _sut.SetDisabledAsync("off-globally", true, TestContext.Current.CancellationToken);
        var overrides = new Dictionary<string, bool> { ["off-globally"] = true, ["on-globally"] = false };

        // Act
        var set = _sut.For(null, overrides);

        // Assert
        set.Skills.Select(s => s.Name).Should().Equal("off-globally");
    }

    [Theory]
    [InlineData(false, null, true)]
    [InlineData(true, null, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    public void IsEnabled_GlobalStateAndOverride_Combine(bool disabledEverywhere, bool? chosen, bool expected)
    {
        // Arrange
        IReadOnlyDictionary<string, bool>? overrides = chosen is { } value ? new Dictionary<string, bool> { ["a"] = value } : null;

        // Act
        var enabled = SkillCatalog.IsEnabled("a", disabledEverywhere, overrides);

        // Assert
        enabled.Should().Be(expected);
    }
}
