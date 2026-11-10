using System.Text;

namespace BlazeHarness.Web.Hosting;

/// <summary>
/// Splits the editor command from the settings into a program and its arguments.
/// </summary>
internal static class EditorCommandLine
{
    /// <summary>
    /// Split a command at spaces, keeping text in single or double quotes together.
    /// </summary>
    /// <remarks>
    /// Backslashes are kept as they are, so Windows paths need no escaping. The command never goes through a shell.
    /// </remarks>
    /// <param name="command">The command, such as <c>kate</c> or <c>"C:\Program Files\Notepad++\notepad++.exe" -multiInst</c>.</param>
    /// <returns>The program followed by its arguments; empty when the command is blank.</returns>
    public static IReadOnlyList<string> Split(string? command)
    {
        List<string> parts = [];
        if (string.IsNullOrWhiteSpace(command))
        {
            return parts;
        }

        var current = new StringBuilder();
        var inPart = false;
        char? quote = null;
        foreach (var c in command)
        {
            if (quote is { } open)
            {
                if (c == open)
                {
                    quote = null;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
                inPart = true;
            }
            else if (char.IsWhiteSpace(c))
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
}
