using System.Text;

namespace BlazeHarness.Web.Hosting;

/// <summary>
/// Reads the command of a freedesktop.org desktop entry, the file Linux desktops use to describe an app and how to start it.
/// </summary>
internal static class DesktopEntry
{
    private const string Group = "[Desktop Entry]";

    /// <summary>
    /// Build the command that opens a file with the app a desktop entry describes.
    /// </summary>
    /// <remarks>
    /// Follows the Exec key rules of the Desktop Entry Specification: string escapes first, then double-quote quoting, then field codes.
    /// <c>%f</c>, <c>%F</c>, <c>%u</c> and <c>%U</c> become the path, <c>%%</c> becomes <c>%</c>, and other field codes are dropped.
    /// The path goes last when the command has no file field code.
    /// </remarks>
    /// <param name="content">The text of the <c>.desktop</c> file.</param>
    /// <param name="path">The full path of the file to open.</param>
    /// <returns>The program followed by its arguments, or null when the entry has no command or has to run in a terminal.</returns>
    public static IReadOnlyList<string>? CommandFor(string content, string path)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(path);
        string? exec = null;
        var terminal = false;
        var inGroup = false;
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[', StringComparison.Ordinal))
            {
                inGroup = line == Group;
                continue;
            }

            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (!inGroup || equals < 0)
            {
                continue;
            }

            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (key == "Exec")
            {
                exec = value;
            }
            else if (key == "Terminal")
            {
                terminal = value.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
        }

        if (exec is null || terminal)
        {
            return null;
        }

        List<string> command = [];
        var placed = false;
        foreach (var part in Tokenize(Unescape(exec)))
        {
            if (Expand(part, path, ref placed) is { } expanded)
            {
                command.Add(expanded);
            }
        }

        if (command.Count == 0)
        {
            return null;
        }

        if (!placed)
        {
            command.Add(path);
        }

        return command;
    }

    /// <summary>
    /// Undo the escapes every string value of a desktop entry may hold.
    /// </summary>
    private static string Unescape(string value)
    {
        var text = new StringBuilder(value.Length);
        var i = 0;
        while (i < value.Length)
        {
            if (value[i] != '\\' || i + 1 == value.Length)
            {
                text.Append(value[i]);
                i++;
                continue;
            }

            var escaped = value[i + 1] switch
            {
                's' => " ",
                'n' => "\n",
                't' => "\t",
                'r' => "\r",
                '\\' => "\\",
                var other => "\\" + other,
            };
            text.Append(escaped);
            i += 2;
        }

        return text.ToString();
    }

    /// <summary>
    /// Split an Exec value into arguments, keeping double-quoted text together.
    /// </summary>
    private static List<string> Tokenize(string exec)
    {
        List<string> parts = [];
        var current = new StringBuilder();
        var inPart = false;
        var quoted = false;
        var i = 0;
        while (i < exec.Length)
        {
            var c = exec[i];
            i++;
            if (quoted)
            {
                if (c == '"')
                {
                    quoted = false;
                }
                else if (c == '\\' && i < exec.Length && exec[i] is '"' or '`' or '$' or '\\')
                {
                    current.Append(exec[i]);
                    i++;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
                inPart = true;
            }
            else if (c is ' ' or '\t')
            {
                if (inPart)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                    inPart = false;
                }
            }
            else
            {
                current.Append(c);
                inPart = true;
            }
        }

        if (inPart)
        {
            parts.Add(current.ToString());
        }

        return parts;
    }

    /// <summary>
    /// Replace the field codes in one argument.
    /// </summary>
    /// <returns>The argument, or null when it was only a field code that expands to nothing.</returns>
    private static string? Expand(string part, string path, ref bool placed)
    {
        if (part is "%f" or "%F" or "%u" or "%U" or "%i")
        {
            if (part == "%i" || placed)
            {
                return null;
            }

            placed = true;
            return path;
        }

        if (!part.Contains('%', StringComparison.Ordinal))
        {
            return part;
        }

        var text = new StringBuilder(part.Length);
        var i = 0;
        while (i < part.Length)
        {
            var c = part[i];
            i++;
            if (c != '%' || i == part.Length)
            {
                text.Append(c);
                continue;
            }

            var code = part[i];
            i++;
            if (code == '%')
            {
                text.Append('%');
            }
            else if (code is 'f' or 'F' or 'u' or 'U' && !placed)
            {
                placed = true;
                text.Append(path);
            }
        }

        return text.Length == 0 ? null : text.ToString();
    }
}
