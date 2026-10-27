namespace BlazeHarness.Core.Mcp;

/// <summary>
/// One environment variable or header being edited in the form for a server.
/// </summary>
public sealed class McpDraftEntry
{
    /// <summary>
    /// Gets or sets the name of the variable or header.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the value the user typed. For a secret it is empty unless the user is replacing the stored value.
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the value is kept in the secret store, and <c>mcp.json</c> only names it.
    /// </summary>
    public bool Secret { get; set; }

    /// <summary>
    /// Gets a value indicating whether the secret store already holds a value for this entry, which is kept unless the user types a new one.
    /// </summary>
    public bool HasStoredSecret => StoredVariable is not null;

    internal string? StoredVariable { get; init; }
}
