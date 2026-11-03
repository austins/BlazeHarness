using System.Globalization;

namespace BlazeHarness.Core.Sandbox.Ports;

/// <summary>
/// Finds the process that is inside a sandbox's own network on Linux.
/// </summary>
internal static class NetworkNamespace
{
    private const int MaxProcesses = 256;

    /// <summary>
    /// Find the first process, the given one or one it started, whose network isn't this app's.
    /// </summary>
    /// <param name="pid">The process the sandbox was started as.</param>
    /// <returns>The process, or null when there is none yet.</returns>
    public static int? FindInside(int pid)
    {
        var own = Of(Environment.ProcessId);
        if (own is null)
        {
            return null;
        }

        var queue = new Queue<int>([pid]);
        for (var visited = 0; visited < MaxProcesses && queue.TryDequeue(out var next); visited++)
        {
            if (Of(next) is { } network && network != own)
            {
                return next;
            }

            foreach (var child in Children(next))
            {
                queue.Enqueue(child);
            }
        }

        return null;
    }

    /// <summary>
    /// Get whether a process has ended.
    /// </summary>
    /// <param name="pid">The process.</param>
    /// <returns>True when it is gone or a zombie.</returns>
    public static bool HasEnded(int pid)
    {
        try
        {
            var stat = File.ReadAllText(string.Create(CultureInfo.InvariantCulture, $"/proc/{pid}/stat"));
            var end = stat.LastIndexOf(')');
            return end < 0 || end + 2 >= stat.Length || stat[end + 2] is 'Z' or 'X';
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static string? Of(int pid)
    {
        try
        {
            return new FileInfo(string.Create(CultureInfo.InvariantCulture, $"/proc/{pid}/ns/net")).LinkTarget;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static List<int> Children(int pid)
    {
        List<int> children = [];
        try
        {
            foreach (var task in Directory.EnumerateDirectories(string.Create(CultureInfo.InvariantCulture, $"/proc/{pid}/task")))
            {
                foreach (var id in File.ReadAllText(Path.Combine(task, "children")).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var child))
                    {
                        children.Add(child);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The process ended while it was read.
        }

        return children;
    }
}
