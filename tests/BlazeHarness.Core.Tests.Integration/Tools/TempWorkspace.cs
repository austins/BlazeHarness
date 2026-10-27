using BlazeHarness.Core.Tools;
using System.Text.Json;

namespace BlazeHarness.Core.Tests.Integration.Tools;

/// <summary>
/// A temporary working folder for tool tests, removed when disposed.
/// </summary>
internal sealed class TempWorkspace : IDisposable
{
    private readonly TempFolder _temp = new("bh-tools-");

    public TempWorkspace()
    {
        Context = new ToolContext(_temp.Root);
    }

    public ToolContext Context { get; }

    public string Root => Context.Root;

    public string Write(string relative, string content)
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public string Write(string relative, byte[] content)
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    public static bool CanMakeFifo => !OperatingSystem.IsWindows();

    public string MakeFifo(string relative)
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var mkfifo = System.Diagnostics.Process.Start("mkfifo", path);
        mkfifo.WaitForExit();
        return path;
    }

    public string Read(string relative) => File.ReadAllText(Path.Combine(Root, relative));

    public static ToolArguments Args(object arguments) => ToolArguments.Parse(JsonSerializer.Serialize(arguments));

    public void Dispose()
    {
        _temp.Dispose();
    }
}
