using BlazeHarness.Core.Config;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Tools;
using BlazeHarness.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazeHarness.Core.Tests.Integration.Permissions;

public sealed class PermissionDefaultsTests : IDisposable
{
    private readonly TempFolder _temp = new("bh-defaults-");
    private readonly JsonFileStore<AppConfig> _config;
    private readonly PermissionDefaults _sut;

    public PermissionDefaultsTests()
    {
        _config = new JsonFileStore<AppConfig>(Path.Combine(_temp.Root, "config.json"), NullLogger<JsonFileStore<AppConfig>>.Instance, TimeProvider.System, watch: false);
        _sut = new PermissionDefaults(_config, new AppPaths(Path.Combine(_temp.Root, "app"), _temp.Root));
    }

    public void Dispose()
    {
        _config.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public async Task SetMcpServerTrustedAsync_TrustThenUntrust_UpdatesTheList()
    {
        // Act
        await _sut.SetMcpServerTrustedAsync("files", trusted: true, TestContext.Current.CancellationToken);
        await _sut.SetMcpServerTrustedAsync("files", trusted: true, TestContext.Current.CancellationToken);
        await _sut.SetMcpServerTrustedAsync("web", trusted: true, TestContext.Current.CancellationToken);
        var trusted = _sut.TrustedMcpServers.ToList();
        await _sut.SetMcpServerTrustedAsync("files", trusted: false, TestContext.Current.CancellationToken);

        // Assert
        trusted.Should().Equal("files", "web");
        _sut.TrustedMcpServers.Should().Equal("web");
        _sut.IsMcpServerTrusted("web").Should().BeTrue();
        _sut.IsMcpServerTrusted("files").Should().BeFalse();
    }

    [Fact]
    public void IsMcpServerTrusted_NothingSaved_IsFalse()
    {
        // Assert
        _sut.IsMcpServerTrusted("files").Should().BeFalse();
    }

    [Fact]
    public void ProtectedFolders_NothingSaved_IsTheBuiltInList()
    {
        // Assert
        _sut.UsesDefaultProtectedFolders.Should().BeTrue();
        _sut.ProtectedFolders.Should().Contain(Path.Combine(_temp.Root, ".ssh"));
    }

    [Fact]
    public async Task SaveProtectedFoldersAsync_AList_ReplacesTheBuiltInListKeepsTheAppFolderAndDropsRelativePaths()
    {
        // Arrange
        var mine = Path.Combine(_temp.Root, "mine");

        // Act
        await _sut.SaveProtectedFoldersAsync([mine, "  ", mine, "relative/dir"], TestContext.Current.CancellationToken);

        // Assert
        _sut.UsesDefaultProtectedFolders.Should().BeFalse();
        _sut.ProtectedFolders.Should().Equal(mine);
        var sensitive = _sut.Sensitive();
        sensitive.IsProtected(Path.Combine(mine, "a.txt")).Should().BeTrue();
        sensitive.IsProtected(Path.Combine(_temp.Root, ".ssh", "id_rsa")).Should().BeFalse();
        sensitive.IsProtected(Path.Combine(_temp.Root, "app", "config.json")).Should().BeTrue();
    }

    [Fact]
    public async Task Sensitive_AskedAgain_ReusesTheListUntilTheSavedFoldersChange()
    {
        // Arrange
        var first = _sut.Sensitive("s1");

        // Act
        var again = _sut.Sensitive("s1");
        var other = _sut.Sensitive("s2");
        await _sut.SaveProtectedFoldersAsync([Path.Combine(_temp.Root, "mine")], TestContext.Current.CancellationToken);
        var changed = _sut.Sensitive("s1");

        // Assert
        again.Should().BeSameAs(first);
        other.Should().NotBeSameAs(first);
        changed.Should().NotBeSameAs(first);
        changed.Folders.Should().Contain(WorkspacePath.CanonicalRoot(Path.Combine(_temp.Root, "mine")));
    }

    [Fact]
    public async Task SaveProtectedFoldersAsync_Null_GoesBackToTheBuiltInList()
    {
        // Arrange
        await _sut.SaveProtectedFoldersAsync([], TestContext.Current.CancellationToken);

        // Act
        await _sut.SaveProtectedFoldersAsync(null, TestContext.Current.CancellationToken);

        // Assert
        _sut.UsesDefaultProtectedFolders.Should().BeTrue();
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
        var json = await File.ReadAllTextAsync(Path.Combine(_temp.Root, "config.json"), TestContext.Current.CancellationToken);

        // Assert
        json.Should().Contain("\"filePath\"").And.Contain("\"ask\"");
    }
}
