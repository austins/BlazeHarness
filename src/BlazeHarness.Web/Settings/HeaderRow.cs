namespace BlazeHarness.Web.Settings;

/// <summary>
/// One extra HTTP header row in the editing form.
/// </summary>
public sealed class HeaderRow
{
    /// <summary>
    /// Gets or sets the header name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the header value.
    /// </summary>
    public string Value { get; set; } = string.Empty;
}
