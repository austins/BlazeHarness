using System.Globalization;
using System.Reflection;

namespace BlazeHarness.Web.Resources;

/// <summary>
/// The languages the web UI is translated into, found from the translated resource files built with the app.
/// </summary>
/// <remarks>
/// The build records the culture of each <c>WebStrings.&lt;culture&gt;.resx</c> in an assembly attribute, so adding a
/// translation needs no other registration. The attribute is read rather than the satellite folders because a
/// single-file publish bundles those folders into the executable.
/// </remarks>
internal sealed class UiLanguages
{
    /// <summary>
    /// Gets the culture of the untranslated resources.
    /// </summary>
    internal const string DefaultCulture = "en-US";

    /// <summary>
    /// The assembly metadata key added per translation by the project's AddUiCultureMetadata target.
    /// </summary>
    private const string CultureMetadataKey = "UiCulture";

    private readonly Dictionary<string, UiLanguage> _byCulture;

    /// <summary>
    /// Initializes a new instance of the <see cref="UiLanguages"/> class.
    /// </summary>
    /// <param name="translatedCultures">The cultures that have translated resources.</param>
    internal UiLanguages(IEnumerable<CultureInfo> translatedCultures)
    {
        var translated = translatedCultures
            .Where(culture => !culture.Equals(CultureInfo.InvariantCulture) && culture.Name != DefaultCulture)
            .DistinctBy(culture => culture.Name)
            .OrderBy(culture => culture.Name, StringComparer.Ordinal)
            .Select(culture => new UiLanguage(culture.Name, Capitalize(culture.NativeName, culture)));
        All = [new UiLanguage(DefaultCulture, Capitalize(CultureInfo.GetCultureInfo("en").NativeName, CultureInfo.GetCultureInfo("en"))), .. translated];
        _byCulture = All.ToDictionary(language => language.Culture, StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets the available languages, the default language first.
    /// </summary>
    internal IReadOnlyList<UiLanguage> All { get; }

    /// <summary>
    /// Gets the names of every culture that should be accepted as a UI culture: each language and its parent
    /// cultures, so a request for <c>zh</c> reaches <c>zh-Hans</c>.
    /// </summary>
    internal IReadOnlyList<string> SupportedCultures =>
    [
        .. All
            .SelectMany(language => Lineage(CultureInfo.GetCultureInfo(language.Culture)))
            .Select(culture => culture.Name)
            .Distinct(StringComparer.Ordinal),
    ];

    /// <summary>
    /// Lists the languages of the translated resources the app was built with.
    /// </summary>
    /// <returns>The available languages.</returns>
    internal static UiLanguages Discover() =>
        new(typeof(WebStrings).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(attribute => attribute.Key == CultureMetadataKey && !string.IsNullOrEmpty(attribute.Value))
            .Select(attribute => CultureInfo.GetCultureInfo(attribute.Value!, predefinedOnly: true)));

    /// <summary>
    /// Finds the language the UI is shown in for a culture.
    /// </summary>
    /// <param name="culture">The UI culture of the request.</param>
    /// <returns>The culture name of the matching language, or the default one.</returns>
    internal string Resolve(CultureInfo culture) =>
        Lineage(culture).FirstOrDefault(candidate => _byCulture.ContainsKey(candidate.Name))?.Name ?? DefaultCulture;

    private static IEnumerable<CultureInfo> Lineage(CultureInfo culture)
    {
        for (var current = culture; !current.Equals(CultureInfo.InvariantCulture); current = current.Parent)
        {
            yield return current;
        }
    }

    /// <summary>
    /// Capitalize the native language name for display in a list.
    /// </summary>
    /// <remarks>Native names are lowercase in some languages, such as Russian.</remarks>
    /// <param name="name">The native language name.</param>
    /// <param name="culture">The culture whose casing rules to use.</param>
    private static string Capitalize(string name, CultureInfo culture) =>
        name.Length == 0 ? name : char.ToUpper(name[0], culture) + name[1..];
}
