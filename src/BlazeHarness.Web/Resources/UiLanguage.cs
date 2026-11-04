namespace BlazeHarness.Web.Resources;

/// <summary>
/// A language the web UI is available in.
/// </summary>
/// <param name="Culture">The culture name, for example <c>ru</c> or <c>zh-Hans</c>.</param>
/// <param name="DisplayName">The name of the language in that language.</param>
internal sealed record UiLanguage(string Culture, string DisplayName);
