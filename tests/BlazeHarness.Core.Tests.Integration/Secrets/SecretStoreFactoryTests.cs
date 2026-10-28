using BlazeHarness.Core.Providers;
using BlazeHarness.Core.Secrets;
using BlazeHarness.Tests.Shared;

namespace BlazeHarness.Core.Tests.Integration.Secrets;

public sealed class SecretStoreFactoryTests : IDisposable
{
    private readonly TempFolder _temp = new("bh-factory-");

    [Fact]
    public async Task CreateAsync_OnThisMachine_ChoosesTheStoreForTheOs()
    {
        // Arrange
        var paths = new AppPaths(_temp.Root, _temp.Root);

        // Act
        using var store = await SecretStoreFactory.CreateAsync(paths, TestContext.Current.CancellationToken);

        // Assert
        if (OperatingSystem.IsWindows())
        {
            store.Name.Should().Be("Windows DPAPI");
        }
        else if (OperatingSystem.IsMacOS())
        {
            store.Should().BeOfType<MacKeychainStore>();
        }
        else
        {
            store.Should().Match<ISecretStore>(s => s is LibsecretStore || s is FileSecretStore);
        }

        store.IsSecure.Should().Be(store is not FileSecretStore);
    }

    [Fact]
    public void KeyFor_ProviderOrId_AreTheSame()
    {
        // Arrange
        var provider = new ProviderConfig { Id = "openai", DisplayName = "OpenAI", BaseUrl = new Uri("http://localhost") };

        // Act
        var fromProvider = SecretStoreFactory.KeyFor(provider);

        // Assert
        fromProvider.Should().Be("provider:openai").And.Be(SecretStoreFactory.KeyFor("openai"));
    }

    public void Dispose() => _temp.Dispose();
}
