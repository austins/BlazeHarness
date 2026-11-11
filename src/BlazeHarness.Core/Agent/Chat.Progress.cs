namespace BlazeHarness.Core.Agent;

public sealed partial class Chat : IToolProgress
{
    /// <inheritdoc />
    void IToolProgress.ReportLine(string callId, string line)
    {
        if (RunningCall(callId) is not { } call)
        {
            return;
        }

        call.AppendLiveLine(line);
        RaiseChanged();
    }

    /// <summary>
    /// Find a call that is running.
    /// </summary>
    /// <remarks>
    /// A running call is in the newest entries, so the search starts at the end; a line that comes after the call ended is ignored.
    /// </remarks>
    private ToolCallEntry? RunningCall(string callId)
    {
        var entries = Entries;
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            foreach (var call in entries[i].ToolCalls)
            {
                if (call.CallId == callId)
                {
                    return call.Status == ToolCallStatus.Running ? call : null;
                }
            }
        }

        return null;
    }
}
