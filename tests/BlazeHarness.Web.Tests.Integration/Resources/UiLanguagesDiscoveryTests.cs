using BlazeHarness.Tests.Shared;
using BlazeHarness.Web.Resources;

namespace BlazeHarness.Web.Tests.Integration.Resources;

public sealed class UiLanguagesDiscoveryTests
{
    [Fact]
    public void Discover_FoldersWithSatelliteAssemblies_ListsOnlyThoseCultures()
    {
        // Arrange
        using var folder = new TempFolder();
        var satellite = typeof(WebStrings).Assembly.GetName().Name + ".resources.dll";
        File.WriteAllText(Path.Combine(folder.CreateDirectory("ru"), satellite), string.Empty);
        File.WriteAllText(Path.Combine(folder.CreateDirectory("not-a-culture-at-all"), satellite), string.Empty);
        folder.CreateDirectory("fr");

        // Act
        var languages = UiLanguages.Discover(folder.Root);

        // Assert
        languages.All.Select(language => language.Culture).Should().Equal("en-US", "ru");
    }

    [Fact]
    public void Discover_AppDirectory_FindsEveryTranslatedResourceFile()
    {
        // Arrange
        var translated = Directory
            .EnumerateFiles(Path.Combine(WebProject(), "Resources"), "WebStrings.*.resx")
            .Select(file => Path.GetFileNameWithoutExtension(file)["WebStrings.".Length..]);

        // Act
        var languages = UiLanguages.Discover(AppContext.BaseDirectory);

        // Assert
        translated.Except(languages.All.Select(language => language.Culture)).Should().BeEmpty();
    }

    private static string WebProject()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "BlazeHarness.Web", "BlazeHarness.Web.csproj")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, "src", "BlazeHarness.Web");
    }
}
