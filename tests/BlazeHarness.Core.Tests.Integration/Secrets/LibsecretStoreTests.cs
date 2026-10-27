using BlazeHarness.Core.Secrets;

namespace BlazeHarness.Core.Tests.Integration.Secrets;

public sealed class LibsecretStoreTests
{
    private const string OptInVariable = "BLAZEHARNESS_TEST_KEYRING";

    // Writes to the real user keyring, so it only runs when explicitly requested.
    public static bool IsEnabled => Environment.GetEnvironmentVariable(OptInVariable) == "1";

    [Fact(Skip = $"Set {OptInVariable}=1 to run against the real Secret Service.", SkipUnless = nameof(IsEnabled))]
    public async Task SetGetDelete_RoundTripsThroughSecretService()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var sut = new LibsecretStore("blazeharness-test-" + Guid.NewGuid().ToString("N"));

        try
        {
            // Act
            await sut.SetAsync("k", "p@ss \"word\"\nline2", ct);
            var value = await sut.GetAsync("k", ct);
            await sut.DeleteAsync("k", ct);
            var after = await sut.GetAsync("k", ct);

            // Assert
            value.Should().Be("p@ss \"word\"\nline2");
            after.Should().BeNull();
        }
        finally
        {
            await sut.DeleteAsync("k", ct);
        }
    }
}
