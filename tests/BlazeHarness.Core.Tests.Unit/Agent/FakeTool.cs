using BlazeHarness.Core.Tools;
using BlazeHarness.Tests.Shared;
using System.Text.Json;

namespace BlazeHarness.Core.Tests.Unit.Agent;

/// <summary>A tool whose behavior a test supplies, which records how many calls ran at the same time.</summary>
internal sealed class FakeTool : ITool
{
    private readonly Func<ToolArguments, CancellationToken, Task<ToolResult>> _run;
    private int _running;

    public FakeTool(string name, bool isReadOnly, Func<ToolArguments, CancellationToken, Task<ToolResult>>? run = null, IReadOnlyList<string>? pathArguments = null)
    {
        PathArguments = pathArguments ?? [];
        Name = name;
        IsReadOnly = isReadOnly;
        _run = run ?? ((_, _) => Task.FromResult(ToolResult.Ok($"{name} done")));
    }

    public string Name { get; }

    public string Description => "A fake tool.";

    public string InputSchema => """{"type":"object","properties":{}}""";

    public bool IsReadOnly { get; }

    public IReadOnlyList<string> PathArguments { get; }

    public TimeSpan Timeout => TestTimeouts.Default;

    /// <summary>Gets the most calls that were running at once.</summary>
    public int MaxConcurrent { get; private set; }

    /// <summary>Gets how many calls started.</summary>
    public int Started { get; private set; }

    public string Summarize(JsonElement arguments) => $"fake {Name}";

    public async Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var now = Interlocked.Increment(ref _running);
        MaxConcurrent = Math.Max(MaxConcurrent, now);
        Started++;
        try
        {
            return await _run(arguments, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _running);
        }
    }
}
