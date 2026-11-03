namespace BlazeHarness.Core.Web.Browser;

/// <summary>
/// What the browser shows after an action.
/// </summary>
/// <param name="Url">The address of the page now.</param>
/// <param name="Title">The page title, which may be empty.</param>
/// <param name="Status">The HTTP status of the navigation, or null when no navigation just happened.</param>
/// <param name="Snapshot">The page as indented text: roles, names and values of what a person would see.</param>
/// <param name="SnapshotClipped">Whether the snapshot was cut at the length limit.</param>
/// <param name="Problems">Console errors, script errors and failed requests since the last view.</param>
internal sealed record BrowserPageView(Uri Url, string Title, int? Status, string Snapshot, bool SnapshotClipped, IReadOnlyList<string> Problems);
