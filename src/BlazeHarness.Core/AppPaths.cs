using Microsoft.Extensions.Configuration;

namespace BlazeHarness.Core;

/// <summary>
/// The single place that resolves every on-disk location the app uses.
/// </summary>
public sealed class AppPaths
{
    /// <summary>
    /// The configuration key that overrides the data folder. The default host configuration also reads it from the environment variable of the same name.
    /// </summary>
    public const string HomeSettingKey = "BLAZEHARNESS_HOME";

    /// <summary>
    /// Initializes a new instance of the <see cref="AppPaths"/> class.
    /// </summary>
    /// <param name="home">The app data folder.</param>
    /// <param name="userHome">The user's home folder, used to find shared locations such as <c>~/.agents/skills</c>.</param>
    public AppPaths(string home, string userHome)
    {
        Home = Path.GetFullPath(home);
        UserHome = Path.GetFullPath(userHome);
    }

    /// <summary>
    /// Gets the app data folder.
    /// </summary>
    public string Home { get; }

    /// <summary>
    /// Gets the user's home folder.
    /// </summary>
    public string UserHome { get; }

    /// <summary>
    /// Gets the path of <c>config.json</c> (defaults, permissions and switches).
    /// </summary>
    public string ConfigFile => Path.Combine(Home, "config.json");

    /// <summary>
    /// Gets the path of <c>providers.json</c> (providers and their models).
    /// </summary>
    public string ProvidersFile => Path.Combine(Home, "providers.json");

    /// <summary>
    /// Gets the path of <c>mcp.json</c> (MCP server definitions).
    /// </summary>
    public string McpFile => Path.Combine(Home, "mcp.json");

    /// <summary>
    /// Gets the path of <c>folders.json</c> (the registered working folders).
    /// </summary>
    public string FoldersFile => Path.Combine(Home, "folders.json");

    /// <summary>
    /// Gets the path of <c>trust.json</c> (trust decisions for project instructions and MCP servers).
    /// </summary>
    public string TrustFile => Path.Combine(Home, "trust.json");

    /// <summary>
    /// Gets the path of <c>secrets.json</c>, used only when no OS secret store is available.
    /// </summary>
    public string SecretsFile => Path.Combine(Home, "secrets.json");

    /// <summary>
    /// Gets the folder of the user's own skills.
    /// </summary>
    public string SkillsDirectory => Path.Combine(Home, "skills");

    /// <summary>
    /// Gets the shared <c>~/.agents/skills</c> folder.
    /// </summary>
    public string UserAgentsSkillsDirectory => Path.Combine(UserHome, ".agents", "skills");

    /// <summary>
    /// Gets the folder that holds one subfolder per chat session.
    /// </summary>
    public string SessionsDirectory => Path.Combine(Home, "sessions");

    /// <summary>
    /// Gets the folder that holds the log files.
    /// </summary>
    public string LogsDirectory => Path.Combine(Home, "logs");

    /// <summary>
    /// Resolve the paths for the current user, honoring the <see cref="HomeSettingKey"/> override.
    /// </summary>
    /// <param name="configuration">The app configuration, so each host instance can have its own data folder.</param>
    /// <returns>Paths rooted at <c>~/.blazeharness</c>, or at the overridden folder.</returns>
    public static AppPaths FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var overridden = configuration[HomeSettingKey];
        var home = string.IsNullOrWhiteSpace(overridden) ? Path.Combine(userHome, ".blazeharness") : overridden;
        return new AppPaths(home, userHome);
    }

    /// <summary>
    /// Get the folder of a single session.
    /// </summary>
    /// <param name="sessionId">The session identifier. It must be a single valid file name, so it cannot escape <see cref="SessionsDirectory"/>.</param>
    /// <returns>The session's folder path.</returns>
    /// <exception cref="ArgumentException">The identifier is empty, <c>.</c>, <c>..</c>, or contains invalid file name characters.</exception>
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
