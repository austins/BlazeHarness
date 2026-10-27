using BlazeHarness.Core.Secrets;
using System.Runtime.Versioning;

namespace BlazeHarness.Core.Tests.Integration.Secrets;

[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStoreTests : IDisposable
{
    private readonly TempFolder _temp = new("bh-dpapi-");
    private readonly DpapiSecretStore? _sut;

    public DpapiSecretStoreTests()
    {
        if (OperatingSystem.IsWindows())
        {
            _sut = new DpapiSecretStore(Path.Combine(_temp.Root, "secrets.json"));
        }
    }

    public static bool IsNotWindows => !OperatingSystem.IsWindows();

    public void Dispose()
    {
        _sut?.Dispose();
        _temp.Dispose();
    }

    [Fact(Skip = "DPAPI exists only on Windows.", SkipWhen = nameof(IsNotWindows))]
    public async Task SetAsync_Value_IsEncryptedOnDiskAndRoundTrips()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(_temp.Root, "secrets.json");

        // Act
        await _sut!.SetAsync("k", "hunter2", ct);
        var value = await _sut.GetAsync("k", ct);

        // Assert
        value.Should().Be("hunter2");
        (await File.ReadAllTextAsync(path, ct)).Should().NotContain("hunter2");
        _sut.IsSecure.Should().BeTrue();
    }
}
