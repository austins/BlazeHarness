using BlazeHarness.Core.Skills;

namespace BlazeHarness.Core.Tests.Unit.Skills;

public sealed class SkillSetTests
{
    [Fact]
    public void ToPrompt_NoSkills_IsNull()
    {
        // Arrange
        var sut = new SkillSet([]);

        // Act
        var prompt = sut.ToPrompt();

        // Assert
        prompt.Should().BeNull();
    }

    [Fact]
    public void ToPrompt_Skills_ListsThemInNameOrderAndNamesTheTool()
    {
        // Arrange
        var sut = new SkillSet([Skill("zeta", "Last."), Skill("alpha", "First.")]);

        // Act
        var prompt = sut.ToPrompt();

        // Assert
        prompt.Should().Contain("load_skill").And.Contain("/skill-name");
        prompt.IndexOf("- alpha: First.", StringComparison.Ordinal).Should().BeInRange(0, prompt.IndexOf("- zeta: Last.", StringComparison.Ordinal) - 1);
    }

    [Fact]
    public void ToPrompt_SameSkillsInAnotherOrder_IsIdentical()
    {
        // Arrange
        var first = new SkillSet([Skill("a", "A."), Skill("b", "B.")]);
        var second = new SkillSet([Skill("b", "B."), Skill("a", "A.")]);

        // Act
        var same = first.ToPrompt() == second.ToPrompt();

        // Assert
        same.Should().BeTrue();
        first.SameAs(second).Should().BeTrue();
    }

    [Fact]
    public void ToPrompt_LongDescription_IsCutOnItsLine()
    {
        // Arrange
        var sut = new SkillSet([Skill("long", new string('x', 500))]);

        // Act
        var prompt = sut.ToPrompt();

        // Assert
        prompt.Should().Contain("- long: " + new string('x', 199) + "…");
    }

    [Fact]
    public void ToPrompt_ManySkills_FallsBackToNamesThenNotesWhatIsLeft()
    {
        // Arrange
        var sut = new SkillSet(Enumerable.Range(0, 400).Select(i => Skill($"skill-{i:D3}", new string('d', 150))));

        // Act
        var prompt = sut.ToPrompt()!;

        // Assert
        prompt.Length.Should().BeLessThan(SkillSet.MaxListingCharacters + 1000);
        prompt.Should().Contain("- skill-000: ddd").And.Contain("- skill-100" + Environment.NewLine).And.Contain("more skills are installed");
        prompt.Should().NotContain("- skill-100:").And.NotContain("- skill-399");
    }

    [Fact]
    public void ToPrompt_TooManyEvenForNames_SaysHowManyAreNotListed()
    {
        // Arrange
        var sut = new SkillSet(Enumerable.Range(0, 1000).Select(i => Skill($"a-long-skill-name-{i:D4}", "Does something useful.")));

        // Act
        var prompt = sut.ToPrompt()!;

        // Assert
        prompt.Should().MatchRegex(@"\(\d+ more skills are installed but not listed here\.\)");
        prompt.Length.Should().BeLessThan(SkillSet.MaxListingCharacters + 1000);
    }

    [Fact]
    public void SameAs_ChangedDescription_IsFalse()
    {
        // Arrange
        var first = new SkillSet([Skill("a", "One.")]);
        var second = new SkillSet([Skill("a", "Two.")]);

        // Act
        var same = first.SameAs(second);

        // Assert
        same.Should().BeFalse();
        first.SameAs(null).Should().BeFalse();
    }

    [Fact]
    public void ToPrompt_SkillThatDisablesModelInvocation_IsNotListed()
    {
        // Arrange
        var sut = new SkillSet([Skill("open", "Shown."), Hidden("secret")]);

        // Act
        var prompt = sut.ToPrompt();

        // Assert
        prompt.Should().Contain("- open: Shown.").And.NotContain("secret");
    }

    [Fact]
    public void ToPrompt_OnlyHiddenSkills_StillExplainsSlashNames()
    {
        // Arrange
        var sut = new SkillSet([Hidden("secret")]);

        // Act
        var prompt = sut.ToPrompt();

        // Assert
        prompt.Should().Contain("/skill-name").And.Contain("No skills are listed").And.NotContain("secret").And.NotContain("Available skills");
    }

    [Theory]
    [InlineData("/secret", true)]
    [InlineData("/secret do it", true)]
    [InlineData("/secret\nmore", true)]
    [InlineData("/secrets", false)]
    [InlineData("use /secret", false)]
    [InlineData("/nothing", false)]
    [InlineData("/", false)]
    public void StartedIn_Messages_FindsSkillsNamedAtTheStart(string message, bool expected)
    {
        // Arrange
        var sut = new SkillSet([Hidden("secret")]);

        // Act
        var started = sut.StartedIn([message]);

        // Assert
        started.Contains("secret").Should().Be(expected);
    }

    private static SkillInfo Hidden(string name) => new(name, "Hidden.", "/skills/" + name, SkillSource.App, ModelInvocable: false);

    private static SkillInfo Skill(string name, string description) => new(name, description, "/skills/" + name, SkillSource.App);
}
