using BlazeHarness.Core.Secrets;

namespace BlazeHarness.Core.Tests.Integration.Secrets;

public sealed class LibsecretStoreTests
{
    public static bool IsUnavailable { get; } = !LibsecretStore.IsAvailableAsync().GetAwaiter().GetResult();

    [Fact(Skip = "Secret Service not available.", SkipWhen = nameof(IsUnavailable))]
    public async Task SetGetDelete_RoundTripsThroughSecretService()
    {
        // Arrange
        using var sut = new LibsecretStore("blazeharness-test-" + Guid.NewGuid().ToString("N"));
        var ct = TestContext.Current.CancellationToken;

        // Act
        await sut.SetAsync("k", "p@ss \"word\"\nline2", ct);
        var value = await sut.GetAsync("k", ct);
        await sut.DeleteAsync("k", ct);
        var after = await sut.GetAsync("k", ct);

        // Assert
        value.Should().Be("p@ss \"word\"\nline2");
        after.Should().BeNull();
    }
}
