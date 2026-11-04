using BlazeHarness.Web.Resources;

namespace BlazeHarness.Web.Tests.Integration.Resources;

public sealed class UiLanguagesDiscoveryTests
{
    [Fact]
    public void Discover_BuiltApp_ListsEveryTranslatedResourceFile()
    {
        // Arrange
        var translated = Directory
            .EnumerateFiles(Path.Combine(WebProject(), "Resources", "Strings"), "WebStrings.*.resx")
            .Select(file => Path.GetFileNameWithoutExtension(file)["WebStrings.".Length..]);

        // Act
        var languages = UiLanguages.Discover();

        // Assert
        languages.All.Select(language => language.Culture).Should().BeEquivalentTo([UiLanguages.DefaultCulture, .. translated]);
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
