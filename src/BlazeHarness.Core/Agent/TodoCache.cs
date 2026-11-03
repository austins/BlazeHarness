namespace BlazeHarness.Core.Agent;

/// <summary>
/// Remembers the list read from the last <c>update_todos</c> call, so a chat that re-reads its list on every render parses the arguments once.
/// </summary>
internal sealed class TodoCache
{
    private readonly Lock _gate = new();
    private ToolCallEntry? _call;
    private TodoList? _list;

    /// <summary>
    /// Look up the list of a call.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="list">The list read from the call, when it is the remembered one.</param>
    /// <returns>True when the call is the remembered one.</returns>
    public bool TryGet(ToolCallEntry call, out TodoList list)
    {
        lock (_gate)
        {
            list = _list!;
            return _list is not null && ReferenceEquals(_call, call);
        }
    }

    /// <summary>
    /// Remember the list of a call, replacing the previous one.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="list">The list read from its arguments.</param>
    public void Set(ToolCallEntry call, TodoList list)
    {
        lock (_gate)
        {
            _call = call;
            _list = list;
        }
    }
}
