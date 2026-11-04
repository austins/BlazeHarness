using System.Globalization;

namespace BlazeHarness.Web.Resources;

/// <summary>
/// The languages the web UI is translated into, found from the translated resource files built with the app.
/// </summary>
/// <remarks>
/// Each <c>WebStrings.&lt;culture&gt;.resx</c> is compiled into a satellite assembly in a folder named after the
/// culture, so adding a translation needs no other registration.
/// </remarks>
internal sealed class UiLanguages
{
    /// <summary>
    /// Gets the culture of the untranslated resources.
    /// </summary>
    internal const string DefaultCulture = "en-US";

    private static readonly string SatelliteFileName = typeof(WebStrings).Assembly.GetName().Name + ".resources.dll";

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
    /// Finds the folder of translated resources under a directory and lists the languages they hold.
    /// </summary>
    /// <param name="directory">The directory the app runs from.</param>
    /// <returns>The available languages.</returns>
    internal static UiLanguages Discover(string directory)
    {
        var cultures = new List<CultureInfo>();
        foreach (var folder in Directory.EnumerateDirectories(directory))
        {
            if (!File.Exists(Path.Combine(folder, SatelliteFileName)))
            {
                continue;
            }

            try
            {
                cultures.Add(CultureInfo.GetCultureInfo(Path.GetFileName(folder), predefinedOnly: true));
            }
            catch (CultureNotFoundException)
            {
                // A folder that isn't named after a culture isn't a translation.
            }
        }

        return new UiLanguages(cultures);
    }

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

    // Native names are lower case in some languages, such as Russian; a list reads better with a capital.
    private static string Capitalize(string name, CultureInfo culture) =>
        name.Length == 0 ? name : char.ToUpper(name[0], culture) + name[1..];
}
