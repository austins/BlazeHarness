using BlazeHarness.Core.Skills;

namespace BlazeHarness.Core.Tests.Unit.Skills;

public sealed class SkillResolutionTests
{
    [Fact]
    public void Of_SameNameInSeveralFolders_ProjectBeatsAppBeatsUserAgents()
    {
        // Arrange
        var user = Scan(Skill("review", SkillSource.UserAgents), Skill("only-user", SkillSource.UserAgents));
        var app = Scan(Skill("review", SkillSource.App));
        var project = Scan(Skill("review", SkillSource.Project));

        // Act
        var result = SkillResolution.Of(user, app, project);

        // Assert
        result.Skills.Select(s => (s.Name, s.Source)).Should().Equal(("only-user", SkillSource.UserAgents), ("review", SkillSource.Project));
        result.Shadowed.Select(s => s.Source).Should().Equal(SkillSource.App, SkillSource.UserAgents);
    }

    [Fact]
    public void Of_AnyInputOrder_SortsByNameOrdinally()
    {
        // Arrange
        var scan = Scan(Skill("b", SkillSource.App), Skill("a-b", SkillSource.App), Skill("a", SkillSource.App));

        // Act
        var result = SkillResolution.Of(scan);

        // Assert
        result.Skills.Select(s => s.Name).Should().Equal("a", "a-b", "b");
    }

    [Fact]
    public void Of_Problems_AreCollectedFromEveryScan()
    {
        // Arrange
        var first = new SkillScan([], ["one: bad"]);
        var second = new SkillScan([], ["two: bad"]);

        // Act
        var result = SkillResolution.Of(first, second);

        // Assert
        result.Problems.Should().Equal("one: bad", "two: bad");
    }

    private static SkillInfo Skill(string name, SkillSource source) => new(name, "does " + name, "/skills/" + source + "/" + name, source);

    private static SkillScan Scan(params SkillInfo[] skills) => new(skills, []);
}
