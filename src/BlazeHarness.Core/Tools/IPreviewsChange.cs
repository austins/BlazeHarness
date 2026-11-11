using BlazeHarness.Core.Diff;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// A tool that can show how a call would change a file before the call runs.
/// </summary>
internal interface IPreviewsChange
{
    /// <summary>
    /// Work out how a call would change its file, without changing anything.
    /// </summary>
    /// <param name="arguments">The arguments of the call.</param>
    /// <param name="context">The context the call would run with.</param>
    /// <returns>The lines of the file, each marked as kept, removed or added, or null when there is nothing worth showing.</returns>
    public IReadOnlyList<DiffLine>? PreviewChange(ToolArguments arguments, ToolContext context);
}
