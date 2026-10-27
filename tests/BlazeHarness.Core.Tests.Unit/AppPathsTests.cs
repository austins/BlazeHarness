namespace BlazeHarness.Core.Tests.Unit;

public sealed class AppPathsTests
{
    private readonly AppPaths _sut = new(Path.Combine(Path.GetTempPath(), "bh-home"), Path.Combine(Path.GetTempPath(), "bh-user"));

    [Fact]
    public void Files_Default_AreUnderHome()
    {
        // Act
        var config = _sut.ConfigFile;

        // Assert
        config.Should().Be(Path.Combine(_sut.Home, "config.json"));
        _sut.SessionsDirectory.Should().StartWith(_sut.Home);
    }

    [Fact]
    public void UserAgentsSkillsDirectory_Default_IsUnderUserHome()
    {
        // Assert
        _sut.UserAgentsSkillsDirectory.Should().Be(Path.Combine(_sut.UserHome, ".agents", "skills"));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\0b")]
    [InlineData("")]
    public void SessionDirectory_InvalidId_Throws(string id)
    {
        // Act
        var act = () => _sut.SessionDirectory(id);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SessionDirectory_ValidId_IsUnderSessions()
    {
        // Act
        var dir = _sut.SessionDirectory("abc123");

        // Assert
        dir.Should().Be(Path.Combine(_sut.SessionsDirectory, "abc123"));
    }
}
