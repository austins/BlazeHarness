using System.Text;
using System.Text.RegularExpressions;

namespace BlazeHarness.Core.Permissions;

/// <summary>
/// Splits a shell command line into the simple commands it runs, so each can be checked on its own.
/// </summary>
/// <remarks>
/// It follows quotes, escapes, pipelines, <c>&amp;&amp;</c> chains, command substitution, wrapper programs such as <c>env</c> and <c>nohup</c>,
/// commands passed to <c>bash -c</c>, <c>pwsh -Command</c> or <c>cmd /c</c>, and <c>find -exec</c>. Text that is built at run time
/// (<c>eval</c>, encoded PowerShell, piping into a shell) is reported as opaque, because no one can tell what it will run.
/// It reads the Bourne shell family and PowerShell well enough to catch deliberate rewrites, but it isn't a full parser.
/// </remarks>
internal static partial class ShellCommandParser
{
    private const int MaxDepth = 6;

    private const string BuiltAtRunTime = "Builds the command to run from text that can't be checked beforehand.";
    private const string PipedText = "Runs piped-in or encoded text as commands, which can't be checked beforehand.";
    private const string TooDeep = "Nests commands too deeply to check beforehand.";

    private static readonly string[] Reserved = ["{", "}", "!", "if", "then", "else", "elif", "do", "while", "until"];
    private static readonly string[] PosixShells = ["sh", "bash", "zsh", "dash", "ksh", "fish", "ash"];
    private static readonly string[] PowerShells = ["pwsh", "powershell", "pwsh-preview"];
    private static readonly string[] DynamicPrograms = ["eval", "invoke-expression", "iex"];
    private static readonly string[] CmdSwitches = ["/c", "/k", "/r"];
    private static readonly string[] FindExecutors = ["-exec", "-execdir", "-ok", "-okdir"];
    private static readonly string[] Extensions = [".exe", ".cmd", ".bat", ".com"];
    private static readonly string[] NoOptionValues = [];

    private static readonly Dictionary<string, string[]> Wrappers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["env"] = ["-u", "-C", "-S"],
        ["command"] = NoOptionValues,
        ["builtin"] = NoOptionValues,
        ["exec"] = NoOptionValues,
        ["nohup"] = NoOptionValues,
        ["busybox"] = NoOptionValues,
        ["toybox"] = NoOptionValues,
        ["time"] = NoOptionValues,
        ["setsid"] = NoOptionValues,
        ["nice"] = ["-n"],
        ["ionice"] = ["-c", "-n"],
        ["timeout"] = ["-s", "-k"],
        ["stdbuf"] = ["-i", "-o", "-e"],
        ["xargs"] = ["-I", "-L", "-P", "-n", "-d", "-E", "-s", "-a"],
    };

    /// <summary>
    /// Split a command line.
    /// </summary>
    /// <param name="command">The command text the model passed to the shell tool.</param>
    /// <returns>The simple commands in it, including those inside substitutions and nested shells.</returns>
    public static IReadOnlyList<CommandSegment> Parse(string command)
    {
        var segments = new List<CommandSegment>();
        new Scanner(command ?? string.Empty, 0, segments).Run();
        return segments;
    }

    /// <summary>
    /// Get the name that identifies a program, without its folder and without <c>.exe</c> style endings. Compare names ignoring case.
    /// </summary>
    /// <param name="token">The program as typed, such as <c>/bin/rm</c> or <c>C:\Windows\System32\cmd.exe</c>.</param>
    /// <returns>The plain name, in the case it was typed.</returns>
    public static string ProgramName(string token)
    {
        var name = token[(token.LastIndexOfAny(['/', '\\']) + 1)..];
        var extension = Extensions.FirstOrDefault(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));
        return extension is null ? name : name[..^extension.Length];
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*\+?=", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Assignment();

    [GeneratedRegex(@"^\d+(\.\d+)?[smhd]?$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Duration();

    [GeneratedRegex(@"^[A-Za-z]:", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DrivePath();

    private static void AddSegment(List<string> tokens, bool fromPipe, int depth, List<CommandSegment> output)
    {
        var index = 0;
        while (index < tokens.Count)
        {
            var name = ProgramName(tokens[index]);
            if (Assignment().IsMatch(tokens[index]) || Reserved.Contains(tokens[index], StringComparer.Ordinal))
            {
                index++;
            }
            else if (Wrappers.TryGetValue(name, out var valued))
            {
                index = SkipWrapperOptions(tokens, index + 1, name, valued);
            }
            else
            {
                break;
            }
        }

        if (index >= tokens.Count)
        {
            return;
        }

        var program = tokens[index];
        var arguments = tokens.GetRange(index + 1, tokens.Count - index - 1);
        if (IsComputed(program))
        {
            output.Add(new CommandSegment(ProgramName(program), arguments, BuiltAtRunTime));
            return;
        }

        var programName = ProgramName(program);
        output.Add(new CommandSegment(programName, arguments));
        AddNested(programName, arguments, fromPipe, depth, output);
    }

    private static bool IsComputed(string program)
    {
        return program.StartsWith("$'", StringComparison.Ordinal)
            || ((program.StartsWith('$') || program.StartsWith('`')) && !program.Contains('/', StringComparison.Ordinal) && !program.Contains('\\', StringComparison.Ordinal));
    }

    private static int SkipWrapperOptions(List<string> tokens, int start, string wrapper, string[] valued)
    {
        var index = start;
        while (index < tokens.Count)
        {
            var token = tokens[index];
            if (token.Length > 1 && token[0] == '-')
            {
                index += Array.IndexOf(valued, token) >= 0 ? 2 : 1;
            }
            else if ((wrapper.Equals("timeout", StringComparison.OrdinalIgnoreCase) || wrapper.Equals("nice", StringComparison.OrdinalIgnoreCase)) && Duration().IsMatch(token))
            {
                index++;
            }
            else
            {
                break;
            }
        }

        return index;
    }

    private static void AddNested(string program, List<string> arguments, bool fromPipe, int depth, List<CommandSegment> output)
    {
        if (DynamicPrograms.Contains(program, StringComparer.OrdinalIgnoreCase))
        {
            output.Add(new CommandSegment(program, arguments, BuiltAtRunTime));
        }
        else if (PosixShells.Contains(program, StringComparer.OrdinalIgnoreCase))
        {
            AddPosixShell(program, arguments, fromPipe, depth, output);
        }
        else if (PowerShells.Contains(program, StringComparer.OrdinalIgnoreCase))
        {
            AddPowerShell(program, arguments, fromPipe, depth, output);
        }
        else if (program.Equals("cmd", StringComparison.OrdinalIgnoreCase))
        {
            var index = arguments.FindIndex(a => CmdSwitches.Contains(a, StringComparer.OrdinalIgnoreCase));
            if (index >= 0)
            {
                Rescan(string.Join(' ', arguments.Skip(index + 1)), depth, output);
            }
        }
        else if (program.Equals("find", StringComparison.OrdinalIgnoreCase))
        {
            AddFindExecutions(arguments, depth, output);
        }
    }

    private static void AddPosixShell(string program, List<string> arguments, bool fromPipe, int depth, List<CommandSegment> output)
    {
        var index = arguments.FindIndex(a => a.Length > 1 && a[0] == '-' && a[1] != '-' && a.Contains('c', StringComparison.Ordinal));
        if (index >= 0)
        {
            if (index + 1 < arguments.Count)
            {
                Rescan(arguments[index + 1], depth, output);
            }
        }
        else if (fromPipe || arguments.Exists(a => a.StartsWith('<') || a == "-s"))
        {
            output.Add(new CommandSegment(program, arguments, PipedText));
        }
    }

    private static void AddPowerShell(string program, List<string> arguments, bool fromPipe, int depth, List<CommandSegment> output)
    {
        for (var i = 0; i < arguments.Count; i++)
        {
            var option = arguments[i];
            if (option.Length < 2 || option[0] != '-')
            {
                continue;
            }

            if ("-encodedcommand".StartsWith(option, StringComparison.OrdinalIgnoreCase))
            {
                output.Add(new CommandSegment(program, arguments, PipedText));
                return;
            }

            if ("-command".StartsWith(option, StringComparison.OrdinalIgnoreCase))
            {
                Rescan(string.Join(' ', arguments.Skip(i + 1)), depth, output);
                return;
            }
        }

        if (fromPipe)
        {
            output.Add(new CommandSegment(program, arguments, PipedText));
        }
    }

    private static void AddFindExecutions(List<string> arguments, int depth, List<CommandSegment> output)
    {
        for (var i = 0; i < arguments.Count; i++)
        {
            if (!FindExecutors.Contains(arguments[i], StringComparer.Ordinal))
            {
                continue;
            }

            var end = arguments.FindIndex(i + 1, a => a is ";" or "+");
            var command = arguments.GetRange(i + 1, (end < 0 ? arguments.Count : end) - i - 1);
            AddSegment(command, false, depth, output);
        }
    }

    private static void Rescan(string script, int depth, List<CommandSegment> output)
    {
        new Scanner(script, depth + 1, output).Run();
    }

    private sealed class Scanner(string text, int depth, List<CommandSegment> output)
    {
        private readonly StringBuilder _token = new();
        private readonly List<string> _tokens = [];
        private bool _inToken;
        private bool _fromPipe;

        public void Run()
        {
            if (depth > MaxDepth)
            {
                output.Add(new CommandSegment(string.Empty, [], TooDeep));
                return;
            }

            var i = 0;
            while (i < text.Length)
            {
                i = Step(i);
            }

            EndSegment(false);
        }

        private int Step(int i)
        {
            var c = text[i];
            switch (c)
            {
                case '\\':
                    return Escape(i);
                case '\'':
                    return ReadSingleQuoted(i);
                case '"':
                    return ReadDoubleQuoted(i);
                case '`':
                    return ReadBacktick(i);
                case '$':
                    return ReadDollar(i);
                case ';' or '\n' or '(' or ')':
                    EndSegment(false);
                    return i + 1;
                case '&':
                    return Ampersand(i);
                case '|':
                    return Pipe(i);
                case '#' when !_inToken:
                    var newline = text.IndexOf('\n', i);
                    return newline < 0 ? text.Length : newline;
                case ' ' or '\t' or '\r':
                    EndToken();
                    return i + 1;
                default:
                    Append(c);
                    return i + 1;
            }
        }

        private void Append(char c)
        {
            _token.Append(c);
            _inToken = true;
        }

        private void Append(string value)
        {
            _token.Append(value);
            _inToken = true;
        }

        private void EndToken()
        {
            if (_inToken)
            {
                _tokens.Add(_token.ToString());
                _token.Clear();
                _inToken = false;
            }
        }

        private void EndSegment(bool nextFromPipe)
        {
            EndToken();
            if (_tokens.Count > 0)
            {
                AddSegment(_tokens, _fromPipe, depth, output);
                _tokens.Clear();
            }

            _fromPipe = nextFromPipe;
        }

        private int Escape(int i)
        {
            if (i + 1 >= text.Length)
            {
                return i + 1;
            }

            var next = text[i + 1];
            if (next == '\r' && i + 2 < text.Length && text[i + 2] == '\n')
            {
                return i + 3;
            }

            if (next == '\n')
            {
                return i + 2;
            }

            if (_inToken && DrivePath().IsMatch(_token.ToString()))
            {
                Append('\\');
            }

            Append(next);
            return i + 2;
        }

        private int Ampersand(int i)
        {
            if (i > 0 && text[i - 1] is '>' or '<')
            {
                Append('&');
                return i + 1;
            }

            EndSegment(false);
            return i + 1;
        }

        private int Pipe(int i)
        {
            var doubled = i + 1 < text.Length && text[i + 1] == '|';
            var withError = i + 1 < text.Length && text[i + 1] == '&';
            EndSegment(!doubled);
            return doubled || withError ? i + 2 : i + 1;
        }

        private int ReadSingleQuoted(int i)
        {
            var end = text.IndexOf('\'', i + 1);
            var stop = end < 0 ? text.Length : end;
            Append(text[(i + 1)..stop]);
            return end < 0 ? text.Length : end + 1;
        }

        private int ReadDoubleQuoted(int i)
        {
            _inToken = true;
            var j = i + 1;
            while (j < text.Length && text[j] != '"')
            {
                j = DoubleQuotedStep(j);
            }

            return j + 1;
        }

        private int DoubleQuotedStep(int j)
        {
            var c = text[j];
            if (c == '\\' && j + 1 < text.Length && text[j + 1] is '"' or '\\' or '$' or '`')
            {
                Append(text[j + 1]);
                return j + 2;
            }

            if (c == '$' && j + 1 < text.Length && text[j + 1] == '(')
            {
                return ReadSubstitution(j);
            }

            if (c == '`')
            {
                return ReadBacktick(j);
            }

            Append(c);
            return j + 1;
        }

        private int ReadDollar(int i)
        {
            var next = i + 1 < text.Length ? text[i + 1] : '\0';
            switch (next)
            {
                case '(':
                    return ReadSubstitution(i);
                case '\'':
                    return ReadAnsiCQuoted(i);
                case '{':
                    var close = text.IndexOf('}', i);
                    var stop = close < 0 ? text.Length : close + 1;
                    Append(text[i..stop]);
                    return stop;
                default:
                    Append('$');
                    return i + 1;
            }
        }

        private int ReadAnsiCQuoted(int i)
        {
            var j = i + 2;
            while (j < text.Length && text[j] != '\'')
            {
                j += text[j] == '\\' ? 2 : 1;
            }

            var stop = Math.Min(j, text.Length);
            var body = text[(i + 2)..Math.Min(stop, text.Length)];

            // Escapes such as \x6d are decoded by the shell, so the real text isn't known here. Keep the raw form so it reads as computed.
            Append(body.Contains('\\', StringComparison.Ordinal) ? text[i..Math.Min(stop + 1, text.Length)] : body);
            return Math.Min(stop + 1, text.Length);
        }

        private int ReadSubstitution(int i)
        {
            var nesting = 0;
            var j = i + 1;
            for (; j < text.Length; j++)
            {
                if (text[j] == '(')
                {
                    nesting++;
                }
                else if (text[j] == ')' && --nesting == 0)
                {
                    break;
                }
            }

            var stop = Math.Min(j, text.Length);
            Rescan(text[(i + 2)..Math.Max(i + 2, stop)], depth, output);
            Append(text[i..Math.Min(stop + 1, text.Length)]);
            return Math.Min(stop + 1, text.Length);
        }

        private int ReadBacktick(int i)
        {
            var end = text.IndexOf('`', i + 1);
            var stop = end < 0 ? text.Length : end;
            Rescan(text[(i + 1)..stop], depth, output);
            Append(text[i..Math.Min(stop + 1, text.Length)]);
            return Math.Min(stop + 1, text.Length);
        }
    }
}
