using BlazeHarness.Core.Secrets;

namespace BlazeHarness.Core.Mcp;

/// <summary>
/// Adds, changes and removes servers in <c>mcp.json</c> for the settings form, and keeps their secrets in the secret store.
/// </summary>
/// <remarks>
/// A value marked secret is never written to the file. The file holds a <c>${MCP_...}</c> reference, and <see cref="McpManager"/> fills it in
/// from the secret store when it starts the server.
/// </remarks>
public sealed class McpServerEditor
{
    private readonly McpConfigFile _file;
    private readonly ISecretStore _secrets;
    private readonly McpManager _manager;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpServerEditor"/> class.
    /// </summary>
    /// <param name="file">The file that lists the servers.</param>
    /// <param name="secrets">Where secret values are kept.</param>
    /// <param name="manager">The manager whose connection to a server is stopped when the server changes.</param>
    public McpServerEditor(McpConfigFile file, ISecretStore secrets, McpManager manager)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(manager);
        _file = file;
        _secrets = secrets;
        _manager = manager;
    }

    /// <summary>
    /// Gets the name of the secret store, for the text next to a secret field.
    /// </summary>
    public string SecretStoreName => _secrets.Name;

    /// <summary>
    /// Gets a value indicating whether the secret store encrypts what it keeps.
    /// </summary>
    public bool SecretStoreIsSecure => _secrets.IsSecure;

    /// <summary>
    /// Get a draft for a server that is not in the file yet.
    /// </summary>
    /// <returns>An empty draft for a local server.</returns>
    public static McpServerDraft NewDraft() => new();

    /// <summary>
    /// Get a draft to edit an existing server.
    /// </summary>
    /// <param name="server">The server as it is in the file.</param>
    /// <param name="cancellationToken">A token to cancel the lookup of stored secrets.</param>
    /// <returns>A draft whose secret entries are marked as stored, without their values.</returns>
    public async Task<McpServerDraft> OpenAsync(McpServerConfig server, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);

        var draft = new McpServerDraft
        {
            IsNew = false,
            Name = server.Name,
            Transport = server.Transport,
            Command = server.Command ?? string.Empty,
            ArgsText = string.Join('\n', server.Args),
            Cwd = server.Cwd ?? string.Empty,
            Url = server.Url ?? string.Empty,
        };
        foreach (var (key, value) in server.Env)
        {
            draft.Env.Add(await EntryAsync(server.Name, key, value, cancellationToken).ConfigureAwait(false));
        }

        foreach (var (key, value) in server.Headers)
        {
            draft.Headers.Add(await EntryAsync(server.Name, key, value, cancellationToken).ConfigureAwait(false));
        }

        return draft;
    }

    /// <summary>
    /// Save a server to the file.
    /// </summary>
    /// <param name="draft">The settings from the form.</param>
    /// <param name="cancellationToken">A token to cancel the save.</param>
    /// <returns>The problems the user has to fix, or an empty list when the server was saved.</returns>
    public async Task<IReadOnlyList<string>> SaveAsync(McpServerDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var name = draft.Name.Trim();
        if (name.Length == 0)
        {
            return ["The server needs a name."];
        }

        var existing = _file.Parsed.Servers.FirstOrDefault(s => s.Name == name);
        if (draft.IsNew && existing is not null)
        {
            return [$"There is already a server called {name}."];
        }

        List<string> problems = [];
        Dictionary<string, string> env = new(StringComparer.Ordinal);
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        List<(string Variable, string Value)> toStore = [];
        var isLocal = draft.Transport == McpTransport.Stdio;
        await ReadEntriesAsync(name, isLocal ? draft.Env : [], env, "environment variable", toStore, problems, cancellationToken).ConfigureAwait(false);
        await ReadEntriesAsync(name, isLocal ? [] : draft.Headers, headers, "header", toStore, problems, cancellationToken).ConfigureAwait(false);
        if (problems.Count > 0)
        {
            return problems;
        }

        var config = new McpServerConfig(
            name,
            draft.Transport,
            isLocal ? draft.Command.Trim() : null,
            isLocal ? [.. draft.ArgsText.Split('\n').Select(a => a.Trim()).Where(a => a.Length > 0)] : [],
            env,
            isLocal ? NullIfBlank(draft.Cwd) : null,
            isLocal ? null : draft.Url.Trim(),
            headers,
            existing?.Disabled ?? false);

        var merged = McpConfigEditor.Upsert(_file.Text, config);
        if (merged.Text is null)
        {
            return [merged.Error ?? "The server could not be saved."];
        }

        var parsed = McpConfigParser.Parse(merged.Text);
        if (parsed.Errors.Count > 0)
        {
            return parsed.Errors;
        }

        foreach (var (variable, value) in toStore)
        {
            await _secrets.SetAsync(McpSecretVariables.StoreKey(variable), value, cancellationToken).ConfigureAwait(false);
        }

        var saved = await _file.SaveAsync(merged.Text, cancellationToken).ConfigureAwait(false);
        if (saved.Errors.Count > 0)
        {
            return saved.Errors;
        }

        if (existing is not null)
        {
            await DeleteUnusedAsync(existing, config, cancellationToken).ConfigureAwait(false);
        }

        // A changed secret doesn't change the file, so the running connection would keep the old one.
        await _manager.StopAsync(name).ConfigureAwait(false);
        return [];
    }

    /// <summary>
    /// Take a server out of the file and delete its secrets.
    /// </summary>
    /// <param name="name">The name of the server.</param>
    /// <param name="cancellationToken">A token to cancel the removal.</param>
    /// <returns>False when the file has no such server or can't be changed.</returns>
    public async Task<bool> RemoveAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var existing = _file.Parsed.Servers.FirstOrDefault(s => s.Name == name);
        if (existing is null || McpConfigEditor.Remove(_file.Text, name) is not { } text)
        {
            return false;
        }

        var saved = await _file.SaveAsync(text, cancellationToken).ConfigureAwait(false);
        if (saved.Errors.Count > 0)
        {
            return false;
        }

        foreach (var variable in McpSecretVariables.Referenced(existing))
        {
            await _secrets.DeleteAsync(McpSecretVariables.StoreKey(variable), cancellationToken).ConfigureAwait(false);
        }

        await _manager.StopAsync(name).ConfigureAwait(false);
        return true;
    }

    private static string? NullIfBlank(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private async Task<McpDraftEntry> EntryAsync(string server, string key, string value, CancellationToken cancellationToken)
    {
        var variable = McpSecretVariables.NameFor(server, key);
        if (McpSecretVariables.TryParseReference(value, out var referenced)
            && string.Equals(referenced, variable, StringComparison.Ordinal)
            && await _secrets.GetAsync(McpSecretVariables.StoreKey(variable), cancellationToken).ConfigureAwait(false) is not null)
        {
            return new McpDraftEntry { Name = key, Secret = true, StoredVariable = variable };
        }

        return new McpDraftEntry { Name = key, Value = value };
    }

    private async Task ReadEntriesAsync(
        string server,
        IEnumerable<McpDraftEntry> entries,
        Dictionary<string, string> into,
        string kind,
        List<(string Variable, string Value)> toStore,
        List<string> problems,
        CancellationToken cancellationToken)
    {
        HashSet<string> variables = new(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var key = entry.Name.Trim();
            if (key.Length == 0 && entry.Value.Length == 0 && !entry.HasStoredSecret)
            {
                continue;
            }

            if (key.Length == 0)
            {
                problems.Add($"Give each {kind} a name.");
                continue;
            }

            if (into.ContainsKey(key))
            {
                problems.Add($"The {kind} {key} is listed twice.");
                continue;
            }

            if (!entry.Secret)
            {
                into[key] = entry.Value;
                continue;
            }

            var variable = McpSecretVariables.NameFor(server, key);
            if (!variables.Add(variable))
            {
                problems.Add($"The {kind} {key} is too similar to another one to keep its secret apart.");
                continue;
            }

            if (entry.Value.Length == 0 && string.Equals(entry.StoredVariable, variable, StringComparison.Ordinal))
            {
                into[key] = McpSecretVariables.Reference(variable);
                continue;
            }

            var value = entry.Value.Length > 0
                ? entry.Value
                : entry.StoredVariable is { } stored
                    ? await _secrets.GetAsync(McpSecretVariables.StoreKey(stored), cancellationToken).ConfigureAwait(false)
                    : null;
            if (string.IsNullOrEmpty(value))
            {
                problems.Add($"The {kind} {key} is marked secret and needs a value.");
                continue;
            }

            into[key] = McpSecretVariables.Reference(variable);
            toStore.Add((variable, value));
        }
    }

    private async Task DeleteUnusedAsync(McpServerConfig before, McpServerConfig after, CancellationToken cancellationToken)
    {
        var kept = McpSecretVariables.Referenced(after);
        foreach (var variable in McpSecretVariables.Referenced(before).Where(v => !kept.Contains(v)))
        {
            await _secrets.DeleteAsync(McpSecretVariables.StoreKey(variable), cancellationToken).ConfigureAwait(false);
        }
    }
}
