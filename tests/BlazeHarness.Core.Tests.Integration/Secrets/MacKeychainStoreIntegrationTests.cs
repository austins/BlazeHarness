using BlazeHarness.Core.Secrets;

namespace BlazeHarness.Core.Tests.Integration.Secrets;

public sealed class MacKeychainStoreIntegrationTests
{
    private const string OptInVariable = "BLAZEHARNESS_TEST_KEYRING";

    // Writes to the real login keychain, so it only runs on macOS when explicitly requested.
    public static bool IsEnabled => OperatingSystem.IsMacOS() && Environment.GetEnvironmentVariable(OptInVariable) == "1";

    [Fact(Skip = $"Needs macOS and {OptInVariable}=1 to run against the real Keychain.", SkipUnless = nameof(IsEnabled))]
    public async Task SetGetDelete_RoundTripsThroughTheKeychain()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var sut = new MacKeychainStore("blazeharness-test-" + Guid.NewGuid().ToString("N"));

        try
        {
            // Act
            await sut.SetAsync("k", "p@ss \"word\"", ct);
            var value = await sut.GetAsync("k", ct);
            await sut.DeleteAsync("k", ct);
            var after = await sut.GetAsync("k", ct);

            // Assert
            value.Should().Be("p@ss \"word\"");
            after.Should().BeNull();
        }
        finally
        {
            await sut.DeleteAsync("k", ct);
        }
    }
}
