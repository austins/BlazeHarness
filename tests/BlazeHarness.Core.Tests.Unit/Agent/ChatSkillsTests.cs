using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Skills;
using BlazeHarness.Core.Tools;
using BlazeHarness.Tests.Shared.Agent;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Tests.Unit.Agent;

public sealed class ChatSkillsTests
{
    private static readonly SkillSet OneSkill = new([new SkillInfo("report", "Write reports.", "/skills/report", SkillSource.App)]);

    [Fact]
    public async Task SendAsync_SkillsAndTools_ListsThemInThePromptAndOffersTheLoadTool()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("ok")]);
        using var sut = Create(client, tools: true);
        sut.Skills = OneSkill;

        // Act
        await sut.SendAsync("hi", TestTargets.Create());

        // Assert
        client.Requests[0][0].Text.Should().Contain("- report: Write reports.");
        client.Options[0]!.Tools!.Select(t => t.Name).Should().Contain("load_skill");
    }

    [Fact]
    public async Task SendAsync_SkillsWithoutTools_AreLeftOut()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("ok")]);
        using var sut = Create(client, tools: false);
        sut.Skills = OneSkill;

        // Act
        await sut.SendAsync("hi", TestTargets.Create());

        // Assert
        client.Requests[0].Select(m => m.Role).Should().Equal(ChatRole.User);
        client.Options[0]!.Tools.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task SendAsync_EmptySkillSet_AddsNeitherPromptNorTool()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("ok")]);
        using var sut = Create(client, tools: true);
        sut.Skills = new SkillSet([]);

        // Act
        await sut.SendAsync("hi", TestTargets.Create());

        // Assert
        client.Requests[0].Select(m => m.Role).Should().Equal(ChatRole.User);
        client.Options[0]!.Tools.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task SendAsync_SameSkillsTwice_SendsTheSamePromptBothTimes()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("ok")]).Enqueue([ScriptedChatClient.Text("again")]);
        using var sut = Create(client, tools: true);
        sut.Skills = OneSkill;

        // Act
        await sut.SendAsync("one", TestTargets.Create());
        await sut.SendAsync("two", TestTargets.Create());

        // Assert
        client.Requests[1][0].Text.Should().Be(client.Requests[0][0].Text);
    }

    [Fact]
    public async Task SendAsync_OnlyHiddenSkills_OffersTheLoadToolButNeverListsThem()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("ok")]);
        using var sut = Create(client, tools: true);
        sut.Skills = new SkillSet([new SkillInfo("secret", "Hidden.", "/skills/secret", SkillSource.App, ModelInvocable: false)]);

        // Act
        await sut.SendAsync("/secret go", TestTargets.Create());

        // Assert
        client.Requests[0][0].Text.Should().Contain("/skill-name").And.NotContain("- secret");
        client.Options[0]!.Tools!.Select(t => t.Name).Should().Contain("load_skill");
    }

    [Fact]
    public async Task SendAsync_MessageNamesASkill_StartsTheReplyWithItsLoadCall()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("ok")]);
        using var sut = Create(client, tools: true);
        sut.Skills = new SkillSet([new SkillInfo("report", "Reports.", "/skills/report", SkillSource.App)]);

        // Act
        await sut.SendAsync("hello, use /report for this", TestTargets.Create());

        // Assert
        var sent = client.Requests.Single();
        sent[^3].Text.Should().Be("hello, use /report for this");
        var call = sent[^2].Contents.Should().ContainSingle().Which.Should().BeOfType<FunctionCallContent>().Subject;
        call.Name.Should().Be("load_skill");
        call.Arguments!["name"].Should().Be("report");
        sent[^1].Contents.Should().ContainSingle().Which.Should().BeOfType<FunctionResultContent>().Which.CallId.Should().Be(call.CallId);
        sut.Entries.Where(e => e.Role == ChatRole.Assistant).SelectMany(e => e.ToolCalls).Should().ContainSingle(c => c.Name == "load_skill");
    }

    [Fact]
    public async Task SendAsync_MessageWithoutASkillName_MakesNoLoadCall()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("ok")]);
        using var sut = Create(client, tools: true);
        sut.Skills = new SkillSet([new SkillInfo("report", "Reports.", "/skills/report", SkillSource.App)]);

        // Act
        await sut.SendAsync("write a report", TestTargets.Create());

        // Assert
        client.Requests.Single()[^1].Text.Should().Be("write a report");
    }

    private static Chat Create(ScriptedChatClient client, bool tools)
    {
        return new Chat((_, _) => Task.FromResult<IChatClient>(client), TimeProvider.System)
        {
            Tools = tools ? new ToolSet([], new ToolContext(Path.GetTempPath()), TimeProvider.System) : null,
        };
    }
}
