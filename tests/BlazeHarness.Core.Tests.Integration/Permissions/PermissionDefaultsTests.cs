using BlazeHarness.Core.Config;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Providers;

namespace BlazeHarness.Core.Tests.Integration.Permissions;

public sealed class PermissionDefaultsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bh-defaults-" + Guid.NewGuid().ToString("N"));
    private readonly JsonFileStore<AppConfig> _config;
    private readonly PermissionDefaults _sut;

    public PermissionDefaultsTests()
    {
        _config = new JsonFileStore<AppConfig>(Path.Combine(_dir, "config.json"), new LoggerFake<PermissionDefaultsTests>(), watch: false);
        _sut = new PermissionDefaults(_config);
    }

    public void Dispose()
    {
        _config.Dispose();
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(true, AccessLevel.WriteInFolder)]
    [InlineData(false, AccessLevel.ReadOnly)]
    public void For_NothingSaved_GivesTheBuiltInStart(bool hasFolder, AccessLevel expected)
    {
        // Act
        var settings = _sut.For(hasFolder);

        // Assert
        settings.Access.Should().Be(expected);
        settings.Approvals.Should().Be(ApprovalMode.AskFirst);
        _sut.AllowBypass.Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_ReadOnlyAndAutoApprove_AppliesToNewChats()
    {
        // Act
        await _sut.SaveAsync(new PermissionSettings(AccessLevel.ReadOnly, ApprovalMode.AutoApprove), allowBypass: false, TestContext.Current.CancellationToken);

        // Assert
        _sut.For(hasFolder: true).Should().Be(new PermissionSettings(AccessLevel.ReadOnly, ApprovalMode.AutoApprove));
        _sut.AllowBypass.Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_RiskyChoices_AreSavedAsTheBuiltInStart()
    {
        // Act
        await _sut.SaveAsync(new PermissionSettings(AccessLevel.Unrestricted, ApprovalMode.NeverAsk), allowBypass: true, TestContext.Current.CancellationToken);

        // Assert
        _sut.For(hasFolder: true).Should().Be(PermissionSettings.Default);
        _config.Current.DefaultAccess.Should().Be(AccessLevel.WriteInFolder);
        _config.Current.DefaultApprovals.Should().Be(ApprovalMode.AskFirst);
    }

    [Fact]
    public async Task Current_RiskyValuesEditedIntoTheConfig_AreIgnored()
    {
        // Arrange
        await _config.UpdateAsync(c => c with { DefaultAccess = AccessLevel.Unrestricted, DefaultApprovals = ApprovalMode.NeverAsk }, TestContext.Current.CancellationToken);

        // Act
        var settings = _sut.For(hasFolder: true);

        // Assert
        settings.Should().Be(PermissionSettings.Default);
    }

    [Fact]
    public async Task SaveRulesAsync_Rules_AreKeptInOrderWithoutEmptyPatterns()
    {
        // Arrange
        var deny = new PermissionRule(RuleTarget.ShellCommand, PermissionVerdict.Deny, "  git push*  ");
        var empty = new PermissionRule(RuleTarget.FilePath, PermissionVerdict.Ask, "  ");
        var allow = new PermissionRule(RuleTarget.FilePath, PermissionVerdict.Allow, "docs/**");

        // Act
        await _sut.SaveRulesAsync([deny, empty, allow], TestContext.Current.CancellationToken);

        // Assert
        _sut.Rules.Should().Equal(deny with { Pattern = "git push*" }, allow);
    }

    [Fact]
    public async Task SaveRulesAsync_Rules_SurviveReadingTheFileAgain()
    {
        // Arrange
        var rule = new PermissionRule(RuleTarget.FilePath, PermissionVerdict.Ask, "**/.env");
        await _sut.SaveRulesAsync([rule], TestContext.Current.CancellationToken);
        var json = await File.ReadAllTextAsync(Path.Combine(_dir, "config.json"), TestContext.Current.CancellationToken);

        // Assert
        json.Should().Contain("\"filePath\"").And.Contain("\"ask\"");
    }
}
