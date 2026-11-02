using BlazeHarness.Core.Tools.Files;
using System.IO.Enumeration;

namespace BlazeHarness.Core.Tools.Shell;

/// <summary>
/// Finds the files and folders the user's rules refuse, so a sandboxed command can be kept away from them.
/// </summary>
/// <remarks>
/// Each rule is only looked for where it can match. A pattern without a wildcard names one path, which is checked directly,
/// wherever it is. A pattern such as <c>secrets/**</c> refuses a whole folder, which is kept out of reach as one path. Any other
/// pattern is looked for below its fixed leading folders, or in the working folder and the read folders when it starts with a
/// wildcard, and no deeper than it can match unless it holds <c>**</c>.
/// </remarks>
internal static class RefusedFiles
{
    /// <summary>The most entries a search looks at before it gives up.</summary>
    public const int MaxEntries = 500_000;

    /// <summary>The most refused paths a command is started with.</summary>
    public const int MaxPaths = 1_000;

    private const int Unbounded = int.MaxValue;

    private static readonly EnumerationOptions Options = new() { AttributesToSkip = 0, IgnoreInaccessible = true, RecurseSubdirectories = false };

    private static readonly StringComparer NameComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// Look for the paths the refusing rules match.
    /// </summary>
    /// <param name="context">The call's context, which holds the refusing rules and the folders a command may read.</param>
    /// <param name="maxEntries">The most entries to look at.</param>
    /// <param name="maxPaths">The most refused paths to collect.</param>
    /// <param name="paths">The refused files and folders that exist now. A refused folder is listed without what is inside it.</param>
    /// <param name="tooBroad">The pattern being looked for when a limit was reached, or null.</param>
    /// <param name="cancellationToken">Stops the search.</param>
    /// <returns>False when a limit was reached, so the list would be incomplete and the caller must not run the command.</returns>
    public static bool TryFind(ToolContext context, int maxEntries, int maxPaths, out List<string> paths, out string? tooBroad, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        paths = [];
        tooBroad = null;
        var refused = context.Refused;
        var found = new HashSet<string>(StringComparer.Ordinal);
        var searches = new Dictionary<string, RefusedFilesSearch>(StringComparer.Ordinal);
        var roots = Roots(context);

        foreach (var pattern in refused.Patterns)
        {
            var segments = pattern.Replace('\\', '/').Split('/');
            var wildcard = Array.FindIndex(segments, IsWildcard);
            var prefix = Resolve(refused.Root, string.Join('/', segments[..(wildcard < 0 ? segments.Length : wildcard)]));
            if (wildcard < 0)
            {
                if (prefix is not null && Path.Exists(prefix) && refused.IsRefused(prefix) && !Add(found, prefix, maxPaths))
                {
                    tooBroad = pattern;
                    return false;
                }

                continue;
            }

            var rest = segments[wildcard..];
            var depth = rest.Any(segment => segment.Contains("**", StringComparison.Ordinal)) ? Unbounded : rest.Length;
            var name = IsWildcard(segments[^1]) ? null : segments[^1];
            if (prefix is null)
            {
                foreach (var root in roots)
                {
                    Include(searches, root, depth == Unbounded ? Unbounded : DepthBelow(root, refused.Root, segments.Length), name, pattern);
                }

                continue;
            }

            if (!Directory.Exists(prefix) || context.Sensitive.IsProtected(prefix))
            {
                continue;
            }

            // When any path inside the folder is refused, the folder itself is kept out of reach instead of each file in it.
            if (rest is ["**"] && refused.IsRefused(Path.Combine(prefix, Guid.NewGuid().ToString("N"))))
            {
                if (!Add(found, prefix, maxPaths))
                {
                    tooBroad = pattern;
                    return false;
                }

                continue;
            }

            // A deep search of a folder outside the reachable ones is limited to the reachable folders inside it.
            var bases = depth == Unbounded && !roots.Exists(root => RootedPath.IsInside(root, prefix))
                ? roots.Where(root => RootedPath.IsInside(prefix, root))
                : [prefix];
            foreach (var folder in bases)
            {
                Include(searches, folder, depth, name, pattern);
            }
        }

        var seen = 0;
        foreach (var (folder, search) in Merged(searches))
        {
            if (!Walk(context, folder, search, found, maxEntries, maxPaths, ref seen, cancellationToken))
            {
                tooBroad = search.Pattern;
                return false;
            }
        }

        paths = [.. found];
        return true;
    }

    private static bool Walk(ToolContext context, string start, RefusedFilesSearch search, HashSet<string> found, int maxEntries, int maxPaths, ref int seen, CancellationToken cancellationToken)
    {
        var pending = new Stack<(string Folder, int Depth)>();
        pending.Push((start, 0));
        while (pending.TryPop(out var next))
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<(string Path, string Name, bool IsFolder)> children;
            try
            {
                // A link is not followed: what it points at is reached, and checked, by its own path.
                children = [.. new FileSystemEnumerable<(string Path, string Name, bool IsFolder)>(
                    next.Folder,
                    static (ref entry) => (entry.ToFullPath(), entry.FileName.ToString(), entry.IsDirectory && !entry.Attributes.HasFlag(FileAttributes.ReparsePoint)),
                    Options)];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                if (++seen > maxEntries)
                {
                    return false;
                }

                if (found.Contains(child.Path))
                {
                    continue;
                }

                if ((search.Names is null || search.Names.Contains(child.Name)) && context.Refused.IsRefused(child.Path))
                {
                    if (!Add(found, child.Path, maxPaths))
                    {
                        return false;
                    }

                    continue;
                }

                if (child.IsFolder && next.Depth + 1 < search.Depth && !context.Sensitive.IsProtected(child.Path))
                {
                    pending.Push((child.Path, next.Depth + 1));
                }
            }
        }

        return true;
    }

    private static List<string> Roots(ToolContext context)
    {
        // A folder inside another one to search is reached by that search, so searching it again would only count its entries twice.
        List<string> roots = [context.Root, .. context.ShellReadFolders];
        return [.. roots.Where(root => !roots.Exists(other => other != root && RootedPath.IsInside(other, root))).Distinct(StringComparer.Ordinal)];
    }

    private static void Include(Dictionary<string, RefusedFilesSearch> searches, string folder, int depth, string? name, string pattern)
    {
        if (depth <= 0)
        {
            return;
        }

        HashSet<string>? names = name is null ? null : new(NameComparer) { name };
        searches[folder] = searches.TryGetValue(folder, out var known) ? known.With(depth, names) : new RefusedFilesSearch(depth, names, pattern);
    }

    private static IEnumerable<KeyValuePair<string, RefusedFilesSearch>> Merged(Dictionary<string, RefusedFilesSearch> searches)
    {
        // A search inside a folder that is searched to any depth adds nothing but the names it looks for.
        var deep = searches.Where(pair => pair.Value.Depth == Unbounded).Select(pair => pair.Key).ToList();
        foreach (var (folder, search) in searches.ToList())
        {
            if (deep.Find(outer => outer != folder && searches.ContainsKey(outer) && RootedPath.IsInside(outer, folder)) is { } covering)
            {
                searches[covering] = searches[covering].With(Unbounded, search.Names);
                searches.Remove(folder);
            }
        }

        return searches;
    }

    private static int DepthBelow(string folder, string root, int segments)
    {
        // A rule is matched against a path's full form and, inside the working folder, against its relative form, so both are counted.
        var depth = segments - Count(folder);
        if (RootedPath.IsInside(root, folder))
        {
            depth = Math.Max(depth, segments - (RootedPath.Relative(root, folder) is "." ? 0 : Count(RootedPath.Relative(root, folder))));
        }
        else if (RootedPath.IsInside(folder, root))
        {
            depth = Math.Max(depth, Count(RootedPath.Relative(folder, root)) + segments);
        }

        return depth;
    }

    private static int Count(string path)
    {
        return path.Replace('\\', '/').TrimEnd('/').Split('/').Length;
    }

    private static string? Resolve(string root, string text)
    {
        if (text.Length == 0 || text.Split('/').Any(segment => segment is "." or ".."))
        {
            return null;
        }

        if (Path.IsPathFullyQualified(text))
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(text));
        }

        // A path rooted but not fully qualified, such as C:x or /x on Windows, depends on the current folder, so it is searched for instead.
        return Path.IsPathRooted(text) ? null : Path.Combine(root, text.Replace('/', Path.DirectorySeparatorChar));
    }

    private static bool IsWildcard(string segment)
    {
        return segment.Contains('*', StringComparison.Ordinal) || segment.Contains('?', StringComparison.Ordinal);
    }

    private static bool Add(HashSet<string> found, string path, int maxPaths)
    {
        return !found.Add(path) || found.Count <= maxPaths;
    }
}
