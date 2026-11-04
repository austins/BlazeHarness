using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Sandbox.Network;

namespace BlazeHarness.Core.Tests.Unit.Sandbox.Network;

public sealed class NetworkHostGateTests
{
    private readonly PermissionEngine _permissions = new();
    private readonly IHostApprover _approver = Substitute.For<IHostApprover>();

    [Fact]
    public async Task RequestAsync_AllowRule_AllowsWithoutAsking()
    {
        // Arrange
        _permissions.Rules = () => [new PermissionRule(RuleTarget.NetworkHost, PermissionVerdict.Allow, "*.npmjs.org")];
        var sut = new NetworkHostGate(_permissions, _approver, "c1");

        // Act
        var access = await sut.RequestAsync("registry.npmjs.org", TestContext.Current.CancellationToken);

        // Assert
        access.Allowed.Should().BeTrue();
        await _approver.DidNotReceiveWithAnyArgs().RequestHostAsync(default!, default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RequestAsync_DenyRule_RefusesAndRecordsTheReason()
    {
        // Arrange
        _permissions.Rules = () => [new PermissionRule(RuleTarget.NetworkHost, PermissionVerdict.Deny, "evil.com")];
        var sut = new NetworkHostGate(_permissions, _approver, "c1");

        // Act
        var access = await sut.RequestAsync("evil.com", TestContext.Current.CancellationToken);

        // Assert
        access.Allowed.Should().BeFalse();
        sut.Refusals.Should().ContainSingle().Which.Should().Contain("evil.com");
    }

    [Fact]
    public async Task RequestAsync_AllowOnce_AsksOncePerHostForTheCall()
    {
        // Arrange
        _approver.RequestHostAsync("c1", "example.com", true, Arg.Any<CancellationToken>()).Returns(ApprovalChoice.AllowOnce);
        var sut = new NetworkHostGate(_permissions, _approver, "c1");

        // Act
        var first = await sut.RequestAsync("example.com", TestContext.Current.CancellationToken);
        var second = await sut.RequestAsync("example.com", TestContext.Current.CancellationToken);

        // Assert
        (first.Allowed, second.Allowed).Should().Be((true, true));
        await _approver.Received(1).RequestHostAsync("c1", "example.com", true, Arg.Any<CancellationToken>());
        _permissions.EvaluateNetworkHost("example.com").Verdict.Should().Be(PermissionVerdict.Ask);
    }

    [Fact]
    public async Task RequestAsync_Deny_RefusesTheHostForTheRestOfTheCall()
    {
        // Arrange
        _approver.RequestHostAsync("c1", "example.com", true, Arg.Any<CancellationToken>()).Returns(ApprovalChoice.Deny);
        var sut = new NetworkHostGate(_permissions, _approver, "c1");

        // Act
        await sut.RequestAsync("example.com", TestContext.Current.CancellationToken);
        var again = await sut.RequestAsync("example.com", TestContext.Current.CancellationToken);

        // Assert
        again.Allowed.Should().BeFalse();
        await _approver.Received(1).RequestHostAsync("c1", "example.com", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestAsync_AllowForChat_RemembersTheHost()
    {
        // Arrange
        _approver.RequestHostAsync("c1", "example.com", true, Arg.Any<CancellationToken>()).Returns(ApprovalChoice.AllowForChat);
        var sut = new NetworkHostGate(_permissions, _approver, "c1");

        // Act
        await sut.RequestAsync("example.com", TestContext.Current.CancellationToken);

        // Assert
        _permissions.EvaluateNetworkHost("example.com").Verdict.Should().Be(PermissionVerdict.Allow);
    }

    [Fact]
    public async Task RequestAsync_AskRule_CannotBeRemembered()
    {
        // Arrange
        _permissions.Rules = () => [new PermissionRule(RuleTarget.NetworkHost, PermissionVerdict.Ask, "example.com")];
        _approver.RequestHostAsync("c1", "example.com", false, Arg.Any<CancellationToken>()).Returns(ApprovalChoice.AllowForChat);
        var sut = new NetworkHostGate(_permissions, _approver, "c1");

        // Act
        var access = await sut.RequestAsync("example.com", TestContext.Current.CancellationToken);

        // Assert
        access.Allowed.Should().BeTrue();
        _permissions.EvaluateNetworkHost("example.com").Verdict.Should().Be(PermissionVerdict.Ask);
    }

    [Fact]
    public async Task RequestAsync_NoApprover_Refuses()
    {
        // Arrange
        var sut = new NetworkHostGate(_permissions, null, "c1");

        // Act
        var access = await sut.RequestAsync("example.com", TestContext.Current.CancellationToken);

        // Assert
        access.Allowed.Should().BeFalse();
        sut.Refusals.Should().ContainSingle();
    }

    [Fact]
    public async Task RequestAsync_NeverAsk_RefusesWithoutAsking()
    {
        // Arrange
        _permissions.Settings = new PermissionSettings(AccessLevel.WriteInFolder, ApprovalMode.NeverAsk);
        var sut = new NetworkHostGate(_permissions, _approver, "c1");

        // Act
        var access = await sut.RequestAsync("example.com", TestContext.Current.CancellationToken);

        // Assert
        access.Allowed.Should().BeFalse();
        await _approver.DidNotReceiveWithAnyArgs().RequestHostAsync(default!, default!, default, TestContext.Current.CancellationToken);
    }
}
