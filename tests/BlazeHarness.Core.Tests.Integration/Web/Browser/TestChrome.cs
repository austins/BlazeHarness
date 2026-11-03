namespace BlazeHarness.Core.Tests.Integration.Web.Browser;

/// <summary>Finds a Chrome headless shell on this computer for the tests that drive a real browser.</summary>
internal static class TestChrome
{
    /// <summary>Gets the path of a Chrome headless shell program, or null when none is installed.</summary>
    public static string? Path { get; } = Find();

    /// <summary>Gets a value indicating whether a headless shell was found; the skip condition for browser tests.</summary>
    public static bool IsAvailable => Path is not null;

    private static string? Find()
    {
        if (Environment.GetEnvironmentVariable("BLAZEHARNESS_TEST_CHROME") is { Length: > 0 } configured && File.Exists(configured))
        {
            return configured;
        }

        var cache = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "ms-playwright");
        return Directory.Exists(cache)
            ? Directory.EnumerateDirectories(cache, "chromium_headless_shell-*")
                .Order(StringComparer.Ordinal)
                .Select(directory => System.IO.Path.Combine(directory, "chrome-headless-shell-linux64", "chrome-headless-shell"))
                .LastOrDefault(File.Exists)
            : null;
    }
}
