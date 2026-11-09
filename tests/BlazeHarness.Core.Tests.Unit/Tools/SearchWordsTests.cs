using BlazeHarness.Core.Tools;

namespace BlazeHarness.Core.Tests.Unit.Tools;

public sealed class SearchWordsTests
{
    [Fact]
    public void Tokenize_MixedText_SplitsCaseAndPunctuationAndDropsFiller()
    {
        // Act
        var words = SearchWords.Tokenize("getUserNames-from_the_db");

        // Assert
        words.Should().Equal("get", "user", "name", "db");
    }

    [Fact]
    public void Tokenize_AccentedLatin_MatchesTheUnaccentedWord()
    {
        // Act
        var words = SearchWords.Tokenize("Café NAÏVE résumé");

        // Assert
        words.Should().Equal("cafe", "naive", "resume");
    }

    [Fact]
    public void Tokenize_DecomposedAccents_MatchTheComposedForm()
    {
        // Act
        var words = SearchWords.Tokenize("cafe\u0301");

        // Assert
        words.Should().Equal("cafe");
    }

    [Fact]
    public void Tokenize_Devanagari_KeepsVowelSignsInTheWord()
    {
        // Act
        var words = SearchWords.Tokenize("हिन्दी भाषा");

        // Assert
        words.Should().Equal("हिन्दी", "भाषा");
    }

    [Fact]
    public void Tokenize_Cyrillic_LowerCasesAndFoldsTheShortI()
    {
        // Act
        var words = SearchWords.Tokenize("Мой ФАЙЛ");

        // Assert
        words.Should().Equal("мои", "фаил");
    }

    [Fact]
    public void Tokenize_Chinese_MatchesPairsOfCharacters()
    {
        // Act
        var words = SearchWords.Tokenize("查询新闻");

        // Assert
        words.Should().Equal("查询", "询新", "新闻");
    }

    [Fact]
    public void Tokenize_Thai_KeepsMarksWithTheirLetters()
    {
        // Act
        var words = SearchWords.Tokenize("ที่");

        // Assert
        words.Should().Equal("ที่");
    }

    [Fact]
    public void Tokenize_CharactersOutsideTheBasicPlane_StayWhole()
    {
        // Act
        var words = SearchWords.Tokenize("𠀀𠀁 plan🙂ning 𝐀𝐁");

        // Assert
        words.Should().Equal("𠀀𠀁", "plan", "ning", "𝐀𝐁");
    }

    [Fact]
    public void Tokenize_CustomStopWords_LeavesOnlyThoseOut()
    {
        // Act
        var words = SearchWords.Tokenize("the tool for files", new HashSet<string>(StringComparer.Ordinal) { "tool" });

        // Assert
        words.Should().Equal("the", "for", "file");
    }

    [Fact]
    public void Tokenize_TurkishCapitalDottedI_MatchesTheLowerCaseWord()
    {
        // Act
        var upper = SearchWords.Tokenize("İstanbul");
        var lower = SearchWords.Tokenize("istanbul");

        // Assert
        upper.Should().Equal(lower);
    }
}
