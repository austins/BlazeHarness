namespace BlazeHarness.Core.Tools;

/// <summary>
/// What a tool call runs against.
/// </summary>
public sealed class ToolContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolContext"/> class.
    /// </summary>
    /// <param name="folder">The working folder, which must exist. Links in it are followed so every later check compares real paths.</param>
    public ToolContext(string folder)
    {
        Root = WorkspacePath.CanonicalRoot(folder);
    }

    /// <summary>
    /// Gets the real path of the working folder, which file tools stay inside and relative paths resolve against.
    /// </summary>
    public string Root { get; }

    /// <summary>
    /// Resolve a path from a call to a real path inside the working folder.
    /// </summary>
    /// <param name="input">The path as the model wrote it.</param>
    /// <param name="fullPath">The real path to use, when the result is true.</param>
    /// <param name="error">A message for the model, when the result is false.</param>
    /// <returns>True when the path is allowed.</returns>
    public bool TryResolve(string? input, out string fullPath, out string error)
    {
        return WorkspacePath.TryResolve(Root, input, out fullPath, out error);
    }

    /// <summary>
    /// Get a path relative to the working folder, written with forward slashes.
    /// </summary>
    /// <param name="fullPath">A path inside the working folder.</param>
    /// <returns>The relative path.</returns>
    public string Relative(string fullPath) => WorkspacePath.Relative(Root, fullPath);
}
