namespace BlazeHarness.Core;

/// <summary>The single place that resolves every on-disk location the app uses.</summary>
public sealed class AppPaths
{
    public const string HomeEnvironmentVariable = "BLAZEHARNESS_HOME";

    public AppPaths(string home, string userHome)
    {
        Home = Path.GetFullPath(home);
        UserHome = Path.GetFullPath(userHome);
    }

    public string Home { get; }

    public string UserHome { get; }

    public string ConfigFile => Path.Combine(Home, "config.json");

    public string McpFile => Path.Combine(Home, "mcp.json");

    public string FoldersFile => Path.Combine(Home, "folders.json");

    public string TrustFile => Path.Combine(Home, "trust.json");

    public string SecretsFile => Path.Combine(Home, "secrets.json");

    public string SkillsDirectory => Path.Combine(Home, "skills");

    public string UserAgentsSkillsDirectory => Path.Combine(UserHome, ".agents", "skills");

    public string SessionsDirectory => Path.Combine(Home, "sessions");

    public static AppPaths FromEnvironment()
    {
        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var overridden = Environment.GetEnvironmentVariable(HomeEnvironmentVariable);
        var home = string.IsNullOrWhiteSpace(overridden) ? Path.Combine(userHome, ".blazeharness") : overridden;
        return new AppPaths(home, userHome);
    }

    public string SessionDirectory(string sessionId)
    {
        return Path.Combine(SessionsDirectory, ValidateId(sessionId));
    }

    private static string ValidateId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.AsSpan().IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id is "." or "..")
        {
            throw new ArgumentException("Invalid identifier.", nameof(id));
        }

        return id;
    }
}
