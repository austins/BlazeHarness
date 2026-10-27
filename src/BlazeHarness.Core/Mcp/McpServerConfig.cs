namespace BlazeHarness.Core.Mcp;

/// <summary>
/// One server from <c>mcp.json</c>, with its values as written, so <c>${VAR}</c> references are not yet replaced.
/// </summary>
/// <param name="Name">The key of the server in the file. It is made of letters, digits, <c>_</c> and <c>-</c>.</param>
/// <param name="Transport">How to reach the server.</param>
/// <param name="Command">The program to start, for a <see cref="McpTransport.Stdio"/> server.</param>
/// <param name="Args">The arguments of the program.</param>
/// <param name="Env">The environment variables to give the program, on top of the baseline.</param>
/// <param name="Cwd">The folder to start the program in, or null for the app's own.</param>
/// <param name="Url">The address of a <see cref="McpTransport.Http"/> or <see cref="McpTransport.Sse"/> server.</param>
/// <param name="Headers">The HTTP headers to send to a remote server.</param>
/// <param name="Disabled">Whether the user turned the server off for every chat.</param>
public sealed record McpServerConfig(
    string Name,
    McpTransport Transport,
    string? Command,
    IReadOnlyList<string> Args,
    IReadOnlyDictionary<string, string> Env,
    string? Cwd,
#pragma warning disable CA1054, CA1056 // Holds the raw mcp.json value, which may contain unexpanded ${VAR} placeholders.
    string? Url,
#pragma warning restore CA1054, CA1056
    IReadOnlyDictionary<string, string> Headers,
    bool Disabled)
{
    /// <summary>
    /// Check whether another configuration starts the same server, comparing lists and maps by content.
    /// </summary>
    /// <param name="other">The configuration to compare with.</param>
    /// <returns>True when the two would start or reach the same server in the same way.</returns>
    public bool SameAs(McpServerConfig? other)
    {
        return other is not null
            && Name == other.Name
            && Transport == other.Transport
            && Command == other.Command
            && Cwd == other.Cwd
            && Url == other.Url
            && Args.SequenceEqual(other.Args)
            && Env.Count == other.Env.Count && Env.All(p => other.Env.TryGetValue(p.Key, out var v) && v == p.Value)
            && Headers.Count == other.Headers.Count && Headers.All(p => other.Headers.TryGetValue(p.Key, out var v) && v == p.Value);
    }

    /// <summary>
    /// Replace <c>${VAR}</c> and <c>${VAR:-default}</c> in the command, arguments, environment, folder, address and headers.
    /// </summary>
    /// <param name="lookup">Returns the value of a variable, or null when it is not set.</param>
    /// <param name="expanded">The server with every reference replaced, when this returns true.</param>
    /// <param name="error">What went wrong, when this returns false.</param>
    /// <returns>False when a variable has no value and no default.</returns>
    public bool TryExpand(Func<string, string?> lookup, out McpServerConfig expanded, out string error)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        string? missing = null;
        string? Replace(string? text)
        {
            if (text is null || missing is not null)
            {
                return text;
            }

            if (McpVariables.TryExpand(text, lookup, out var result, out var name))
            {
                return result;
            }

            missing = name;
            return text;
        }

        var next = this with
        {
            Command = Replace(Command),
            Args = [.. Args.Select(a => Replace(a)!)],
            Env = Env.ToDictionary(p => p.Key, p => Replace(p.Value)!, StringComparer.Ordinal),
            Cwd = Replace(Cwd),
            Url = Replace(Url),
            Headers = Headers.ToDictionary(p => p.Key, p => Replace(p.Value)!, StringComparer.OrdinalIgnoreCase),
        };
        if (missing is not null)
        {
            expanded = this;
            error = $"The variable {missing} is not set and has no default.";
            return false;
        }

        expanded = next;
        error = string.Empty;
        return true;
    }
}
