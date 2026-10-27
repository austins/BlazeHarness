namespace BlazeHarness.Web.Settings;

/// <summary>
/// One custom request field in the sampling editor.
/// </summary>
public sealed class ExtraParamRow
{
    /// <summary>
    /// Gets or sets the field name; a dotted name such as <c>chat_template_kwargs.x</c> sets a nested field.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the value as JSON text; text that is not valid JSON is sent as a string.
    /// </summary>
    public string Json { get; set; } = string.Empty;
}
