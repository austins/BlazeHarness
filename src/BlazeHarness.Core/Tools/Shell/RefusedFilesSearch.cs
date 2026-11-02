namespace BlazeHarness.Core.Tools.Shell;

/// <summary>
/// How far below one folder <see cref="RefusedFiles"/> looks, and for which names.
/// </summary>
/// <param name="Depth">The deepest level to look at, where the folder's own entries are level 1.</param>
/// <param name="Names">The only names a refusing rule can match here, or null when a rule's last part has a wildcard.</param>
/// <param name="Pattern">The first rule that asked for the search, to name when it reaches a limit.</param>
internal sealed record RefusedFilesSearch(int Depth, HashSet<string>? Names, string Pattern)
{
    /// <summary>
    /// Get a search that also covers another rule's depth and names.
    /// </summary>
    /// <param name="depth">The other rule's depth.</param>
    /// <param name="names">The other rule's names, or null for any name.</param>
    /// <returns>The combined search.</returns>
    public RefusedFilesSearch With(int depth, HashSet<string>? names)
    {
        return new RefusedFilesSearch(Math.Max(Depth, depth), Names is null || names is null ? null : new HashSet<string>(Names.Concat(names), Names.Comparer), Pattern);
    }
}
