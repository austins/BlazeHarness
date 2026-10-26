using BlazeHarness.Core.Providers;
using BlazeHarness.Core.Secrets;

namespace BlazeHarness.Core.Tests.Unit;

public sealed class ApiKeyResolverTests
{
    private static readonly ProviderConfig Provider = new() { Id = "p", DisplayName = "P", BaseUrl = "http://x/v1" };

    private readonly ISecretStore _store = Substitute.For<ISecretStore>();

    [Fact]
    public async Task ResolveAsync_NoRef_ReadsSecretStore()
    {
        // Arrange
        _store.GetAsync("provider:p", Arg.Any<CancellationToken>()).Returns("secret");
        var sut = new ApiKeyResolver(_store, _ => null);

        // Act
        var key = await sut.ResolveAsync(Provider, TestContext.Current.CancellationToken);

        // Assert
        key.Should().Be("secret");
    }

    [Fact]
    public async Task ResolveAsync_EnvRef_ReadsEnvironmentAndSkipsStore()
    {
        // Arrange
        var sut = new ApiKeyResolver(_store, name => name == "MY_KEY" ? "from-env" : null);

        // Act
        var key = await sut.ResolveAsync(Provider with { ApiKeyRef = "${MY_KEY}" }, TestContext.Current.CancellationToken);

        // Assert
        key.Should().Be("from-env");
        await _store.DidNotReceiveWithAnyArgs().GetAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ResolveAsync_LiteralRef_IsNotTreatedAsKey()
    {
        // Arrange
        var sut = new ApiKeyResolver(_store, _ => "x");

        // Act
        var key = await sut.ResolveAsync(Provider with { ApiKeyRef = "sk-literal" }, TestContext.Current.CancellationToken);

        // Assert
        key.Should().BeNull();
    }
}
