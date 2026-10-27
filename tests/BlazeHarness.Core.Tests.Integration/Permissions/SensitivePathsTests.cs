using BlazeHarness.Core.Permissions;

namespace BlazeHarness.Core.Tests.Integration.Permissions;

public sealed class SensitivePathsTests
{
    private readonly AppPaths _paths = new(Path.Combine(Path.GetTempPath(), "bh-home"), Path.Combine(Path.GetTempPath(), "bh-user"));

    [Theory]
    [InlineData(".ssh/id_ed25519")]
    [InlineData(".aws/credentials")]
    [InlineData(".gnupg")]
    [InlineData(".microsoft/usersecrets/abc/secrets.json")]
    [InlineData(".config/gh/hosts.yml")]
    [InlineData(".config/gcloud/credentials.db")]
    [InlineData(".password-store/mail.gpg")]
    [InlineData(".local/share/keyrings/login.keyring")]
    [InlineData(".netrc")]
    [InlineData(".git-credentials")]
    [InlineData(".npmrc")]
    public void IsProtected_CredentialFolderOrFileInHome_IsTrue(string relative)
    {
        // Arrange
        var sut = SensitivePaths.Default(_paths);

        // Act
        var result = sut.IsProtected(Path.Combine(_paths.UserHome, relative));

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsProtected_AppDataFolder_IsTrue()
    {
        // Arrange
        var sut = SensitivePaths.Default(_paths);

        // Act
        var result = sut.IsProtected(_paths.ConfigFile);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsProtected_CurrentSessionDirectory_IsFalse()
    {
        // Arrange
        var sut = SensitivePaths.Default(_paths, "s1");

        // Act
        var own = sut.IsProtected(Path.Combine(_paths.SessionDirectory("s1"), "outputs", "a.txt"));
        var other = sut.IsProtected(Path.Combine(_paths.SessionDirectory("s2"), "messages.jsonl"));

        // Assert
        own.Should().BeFalse();
        other.Should().BeTrue();
    }

    [Fact]
    public void IsProtected_OrdinaryProjectFolder_IsFalse()
    {
        // Arrange
        var sut = SensitivePaths.Default(_paths);

        // Act
        var result = sut.IsProtected(Path.Combine(_paths.UserHome, "projects", "app", "a.cs"));

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsProtected_NameSharingAPrefix_IsFalse()
    {
        // Arrange
        var sut = SensitivePaths.Default(_paths);

        // Act
        var result = sut.IsProtected(Path.Combine(_paths.UserHome, ".ssh-notes", "a.txt"));

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void None_ProtectsNothing()
    {
        // Act
        var result = SensitivePaths.None.IsProtected(Path.Combine(_paths.UserHome, ".ssh"));

        // Assert
        result.Should().BeFalse();
    }
}
