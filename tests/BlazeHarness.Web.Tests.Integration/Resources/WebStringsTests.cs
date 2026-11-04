using Jeffijoe.MessageFormat.Formatting;
using Jeffijoe.MessageFormat.Parsing;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BlazeHarness.Web.Tests.Integration.Resources;

public sealed partial class WebStringsTests
{
    [Fact]
    public void EnglishResources_Entries_AreDocumentedAndReferenced()
    {
        // Arrange
        var project = WebProjectDirectory();
        var document = XDocument.Load(Path.Combine(project, "Resources", "WebStrings.resx"));
        var entries = document.Root!.Elements("data").ToArray();
        var keys = entries.Select(entry => entry.Attribute("name")!.Value).ToHashSet(StringComparer.Ordinal);
        var sources = SourceFiles(project);
        var references = ReferencedKeys(sources, @"\bWebStrings\.(\w+)");
        var formatted = ReferencedKeys(sources, @"\bnameof\s*\(\s*WebStrings\.(\w+)\s*\)");

        // Act
        var unused = keys.Except(references).ToArray();
        var undocumented = entries
            .Where(entry => string.IsNullOrWhiteSpace(entry.Element("comment")?.Value))
            .Select(entry => entry.Attribute("name")!.Value)
            .ToArray();
        var indexed = keys.Where(key => IndexedKey().IsMatch(key)).ToArray();
        var boilerplate = entries
            .Where(entry => entry.Element("comment")?.Value.StartsWith("Visible text in", StringComparison.Ordinal) == true)
            .Select(entry => entry.Attribute("name")!.Value)
            .ToArray();
        var plainWithPlaceholders = entries
            .Where(entry => !formatted.Contains(entry.Attribute("name")!.Value) && entry.Element("value")!.Value.Contains('{', StringComparison.Ordinal))
            .Select(entry => entry.Attribute("name")!.Value)
            .ToArray();
        var quotedBraces = entries
            .Where(entry => formatted.Contains(entry.Attribute("name")!.Value) && ApostropheBesideBrace().IsMatch(entry.Element("value")!.Value))
            .Select(entry => entry.Attribute("name")!.Value)
            .ToArray();

        // Assert
        unused.Should().BeEmpty("every English resource should be referenced by the web UI");
        undocumented.Should().BeEmpty("translators need context for each resource");
        indexed.Should().BeEmpty("resource keys should name the text, not number it");
        boilerplate.Should().BeEmpty("comments should explain the text, not repeat it");
        plainWithPlaceholders.Should().BeEmpty("text with placeholders must be read with ResourceMessageFormatter, not a plain property");
        quotedBraces.Should().BeEmpty("an apostrophe next to a brace quotes it in MessageFormat; use typographic quotes around a placeholder");
        foreach (var value in entries.Where(entry => formatted.Contains(entry.Attribute("name")!.Value)).Select(entry => entry.Element("value")!.Value))
        {
            Variables(value);
        }
    }

    [Fact]
    public void TranslatedResources_Entries_UseKnownKeysAndPlaceholders()
    {
        // Arrange
        var project = WebProjectDirectory();
        var resources = Path.Combine(project, "Resources");
        var english = Entries(Path.Combine(resources, "WebStrings.resx"));

        // Act
        foreach (var file in Directory.EnumerateFiles(resources, "WebStrings.*.resx"))
        {
            var translated = Entries(file);

            // Assert
            translated.Keys.Except(english.Keys).Should().BeEmpty($"{file} should not introduce resource keys");
            foreach (var (key, value) in translated.Where(entry => english.ContainsKey(entry.Key)))
            {
                Variables(value).Should().BeEquivalentTo(Variables(english[key]), $"{file}: {key} should preserve its placeholders");
            }
        }
    }

    [Fact]
    public void Variables_NestedPluralAndSelection_FindsEveryPlaceholder()
    {
        // Arrange
        const string pattern = "{count, plural, one {{name} has one} other {{name} has {count} for {owner, select, other {{recipient}}}}}";

        // Act
        var variables = Variables(pattern);

        // Assert
        variables.Should().BeEquivalentTo("count", "name", "owner", "recipient");
    }

    [Theory]
    [InlineData("{count, plural, =1 {exact} one {one} other {other}}", true)]
    [InlineData("{count, plural, one {one} =1 {exact} other {other}}", false)]
    public void ExactCases_InPluralPattern_MustComeFirst(string pattern, bool expected)
    {
        // Arrange
        var request = new PatternParser().Parse(new StringBuilder(pattern)).Single();
        var blocks = new FormatterArgumentsReader().Read(request);

        // Act
        var result = ExactCasesComeFirst(blocks);

        // Assert
        result.Should().Be(expected);
    }

    [GeneratedRegex(@"_\d|^[A-Z][a-z]+_")]
    private static partial Regex IndexedKey();

    [GeneratedRegex("'[{}]|[{}]'")]
    private static partial Regex ApostropheBesideBrace();

    private static Dictionary<string, string> Entries(string path) =>
        XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(entry => entry.Attribute("name")!.Value, entry => entry.Element("value")!.Value, StringComparer.Ordinal);

    private static HashSet<string> Variables(string pattern)
    {
        var variables = new HashSet<string>(StringComparer.Ordinal);
        CollectVariables(pattern, variables);
        return variables;
    }

    private static void CollectVariables(string pattern, HashSet<string> variables)
    {
        var parser = new PatternParser();
        var arguments = new FormatterArgumentsReader();
        foreach (var request in parser.Parse(new StringBuilder(pattern)))
        {
            variables.Add(request.Variable);
            if (request.FormatterName is "plural" or "selectordinal" or "select")
            {
                var blocks = arguments.Read(request).ToArray();
                blocks.Select(block => block.Key).Should().Contain("other", "every selection needs a fallback");
                if (request.FormatterName is "plural" or "selectordinal")
                {
                    ExactCasesComeFirst(blocks).Should().BeTrue("MessageFormat evaluates plural cases in order");
                }

                foreach (var block in blocks)
                {
                    CollectVariables(block.BlockText, variables);
                }
            }
        }
    }

    private static bool ExactCasesComeFirst(IEnumerable<KeyedBlock> blocks) =>
        !blocks.SkipWhile(block => block.Key.StartsWith('=')).Any(block => block.Key.StartsWith('='));

    private static string[] SourceFiles(string project) =>
        Directory.EnumerateFiles(project, "*", SearchOption.AllDirectories).Where(IsSourceFile).Select(File.ReadAllText).ToArray();

    private static HashSet<string> ReferencedKeys(IEnumerable<string> sources, string pattern)
    {
        var reference = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return sources.SelectMany(source => reference.Matches(source).Select(match => match.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool IsSourceFile(string path)
    {
        var separator = Path.DirectorySeparatorChar;
        return !path.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
            && !path.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
            && Path.GetExtension(path) is ".cs" or ".razor" or ".cshtml" or ".js";
    }

    private static string WebProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BlazeHarness.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("The repository root was not found."), "src", "BlazeHarness.Web");
    }

    private sealed class FormatterArgumentsReader : BaseFormatter
    {
        public IEnumerable<KeyedBlock> Read(FormatterRequest request) => ParseArguments(request).KeyedBlocks;
    }
}
