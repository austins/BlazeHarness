using BlazeHarness.Web.Hosting;

namespace BlazeHarness.Web.Tests.Unit.Hosting;

public sealed class LaunchCredentialsTests
{
    private readonly LaunchCredentials _sut = new();

    [Fact]
    public void New_Always_CreatesDistinctLongSecrets()
    {
        // Act
        var other = new LaunchCredentials();

        // Assert
        _sut.Token.Should().HaveLength(64).And.NotBe(_sut.SessionSecret);
        other.Token.Should().NotBe(_sut.Token);
        other.SessionSecret.Should().NotBe(_sut.SessionSecret);
    }

    [Fact]
    public void TryConsumeToken_RightToken_WorksOnlyOnce()
    {
        // Act
        var first = _sut.TryConsumeToken(_sut.Token);
        var second = _sut.TryConsumeToken(_sut.Token);

        // Assert
        first.Should().BeTrue();
        second.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong")]
    public void TryConsumeToken_WrongToken_IsRefusedAndDoesNotBurnTheRealOne(string? candidate)
    {
        // Act
        var wrong = _sut.TryConsumeToken(candidate);
        var right = _sut.TryConsumeToken(_sut.Token);

        // Assert
        wrong.Should().BeFalse();
        right.Should().BeTrue();
    }

    [Fact]
    public async Task TryConsumeToken_ManyAtOnce_AdmitsExactlyOne()
    {
        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => _sut.TryConsumeToken(_sut.Token), TestContext.Current.CancellationToken)));

        // Assert
        results.Count(r => r).Should().Be(1);
    }

    [Fact]
    public void IsValidSession_TheSecret_IsTrueEvenAfterTheTokenIsUsed()
    {
        // Arrange
        _sut.TryConsumeToken(_sut.Token);

        // Act
        var valid = _sut.IsValidSession(_sut.SessionSecret);

        // Assert
        valid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong")]
    public void IsValidSession_AnythingElse_IsFalse(string? cookie)
    {
        // Act
        var valid = _sut.IsValidSession(cookie);

        // Assert
        valid.Should().BeFalse();
    }

    [Fact]
    public void IsValidSession_TheLaunchToken_IsFalse()
    {
        // Act
        var valid = _sut.IsValidSession(_sut.Token);

        // Assert
        valid.Should().BeFalse();
    }
}
