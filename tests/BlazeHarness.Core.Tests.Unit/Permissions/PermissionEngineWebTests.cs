using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Tools;

namespace BlazeHarness.Core.Tests.Unit.Permissions;

public sealed class PermissionEngineWebTests
{
    private readonly PermissionEngine _sut = new();
    private readonly ToolContext _context = new(Path.GetTempPath());
    private readonly WebFetchTool _fetch = new(null!, "session");

    [Fact]
    public void Evaluate_Fetch_AsksAndKeysByHost()
    {
        // Act
        var decision = _sut.Evaluate(_fetch, """{"url":"https://example.com/a/b"}""", _context);

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Ask);
        decision.GrantKey.Should().Be("web:host:example.com");
    }

    [Fact]
    public void Evaluate_FetchAfterRemember_Allows()
    {
        // Arrange
        _sut.Remember("web:host:example.com");

        // Act
        var decision = _sut.Evaluate(_fetch, """{"url":"https://example.com/other"}""", _context);

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Allow);
    }

    [Fact]
    public void Evaluate_Unrestricted_Allows()
    {
        // Arrange
        _sut.Settings = new PermissionSettings(AccessLevel.Unrestricted, ApprovalMode.AskFirst);

        // Act
        var decision = _sut.Evaluate(_fetch, """{"url":"https://example.com"}""", _context);

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Allow);
    }

    [Fact]
    public void Evaluate_DenyRuleForHost_Denies()
    {
        // Arrange
        _sut.Rules = () => [new PermissionRule(RuleTarget.WebHost, PermissionVerdict.Deny, "*.example.com")];

        // Act
        var decision = _sut.Evaluate(_fetch, """{"url":"https://docs.example.com"}""", _context);

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Deny);
    }

    [Fact]
    public void Evaluate_MalformedUrl_Allows()
    {
        // Act
        var decision = _sut.Evaluate(_fetch, """{"url":"file:///etc/passwd"}""", _context);

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Allow);
    }

    [Theory]
    [InlineData("Evil.com.")]
    [InlineData("EVIL.COM")]
    public void Evaluate_DenyRule_MatchesTrailingDotAndCase(string host)
    {
        // Arrange
        _sut.Rules = () => [new PermissionRule(RuleTarget.WebHost, PermissionVerdict.Deny, "Evil.com")];

        // Act
        var decision = _sut.Evaluate(_fetch, $$"""{"url":"https://{{host}}/x"}""", _context);

        // Assert
        decision.Verdict.Should().Be(PermissionVerdict.Deny);
    }
}
