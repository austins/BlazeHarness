using BlazeHarness.Core.Permissions;

namespace BlazeHarness.Core.Tests.Unit.Permissions;

public sealed class PermissionEngineNetworkTests
{
    private readonly PermissionEngine _sut = new();

    [Theory]
    [InlineData(ApprovalMode.AskFirst)]
    [InlineData(ApprovalMode.AutoApprove)]
    public void EvaluateNetworkHost_UnlistedHost_AsksAndKeysByHost(ApprovalMode approvals)
    {
        // Arrange
        _sut.Settings = new PermissionSettings(AccessLevel.WriteInFolder, approvals);

        // Act
        var decision = _sut.EvaluateNetworkHost("Registry.NPMJS.org");

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Ask);
        decision.GrantKey.Should().Be("net:registry.npmjs.org");
    }

    [Fact]
    public void EvaluateNetworkHost_NeverAsk_RefusesAnUnlistedHost()
    {
        // Arrange
        _sut.Settings = new PermissionSettings(AccessLevel.WriteInFolder, ApprovalMode.NeverAsk);

        // Act
        var decision = _sut.EvaluateNetworkHost("example.com");

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Deny);
    }

    [Fact]
    public void EvaluateNetworkHost_NeverAskWithAllowRule_Allows()
    {
        // Arrange
        _sut.Settings = new PermissionSettings(AccessLevel.WriteInFolder, ApprovalMode.NeverAsk);
        _sut.Rules = () => [new PermissionRule(RuleTarget.NetworkHost, PermissionVerdict.Allow, "*.npmjs.org")];

        // Act
        var decision = _sut.EvaluateNetworkHost("registry.npmjs.org");

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Allow);
    }

    [Fact]
    public void EvaluateNetworkHost_DenyRuleBeatsAnAllowRuleAndAGrant()
    {
        // Arrange
        _sut.Remember("net:evil.example.com");
        _sut.Rules = () =>
        [
            new PermissionRule(RuleTarget.NetworkHost, PermissionVerdict.Allow, "*.example.com"),
            new PermissionRule(RuleTarget.NetworkHost, PermissionVerdict.Deny, "evil.example.com"),
        ];

        // Act
        var decision = _sut.EvaluateNetworkHost("evil.example.com");

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Deny);
    }

    [Fact]
    public void EvaluateNetworkHost_AskRule_AsksEveryTimeEvenAfterAGrant()
    {
        // Arrange
        _sut.Remember("net:example.com");
        _sut.Rules = () => [new PermissionRule(RuleTarget.NetworkHost, PermissionVerdict.Ask, "example.com")];

        // Act
        var decision = _sut.EvaluateNetworkHost("example.com");

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Ask);
        decision.GrantKey.Should().BeNull();
    }

    [Fact]
    public void EvaluateNetworkHost_AskRuleWhenNeverAsk_Refuses()
    {
        // Arrange
        _sut.Settings = new PermissionSettings(AccessLevel.WriteInFolder, ApprovalMode.NeverAsk);
        _sut.Rules = () => [new PermissionRule(RuleTarget.NetworkHost, PermissionVerdict.Ask, "example.com")];

        // Act
        var decision = _sut.EvaluateNetworkHost("example.com");

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Deny);
    }

    [Fact]
    public void EvaluateNetworkHost_AfterRemember_Allows()
    {
        // Arrange
        _sut.Remember("net:example.com");

        // Act
        var decision = _sut.EvaluateNetworkHost("EXAMPLE.com");

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Allow);
    }

    [Fact]
    public void EvaluateNetworkHost_WebHostRule_DoesNotApply()
    {
        // Arrange
        _sut.Rules = () => [new PermissionRule(RuleTarget.WebHost, PermissionVerdict.Allow, "example.com")];

        // Act
        var decision = _sut.EvaluateNetworkHost("example.com");

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Ask);
    }
}
