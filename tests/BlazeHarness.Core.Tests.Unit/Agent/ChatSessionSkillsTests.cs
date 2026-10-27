using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Skills;
using BlazeHarness.Core.Tools;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Tests.Unit.Agent;

public sealed class ChatSessionSkillsTests
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

    private static ChatSession Create(ScriptedChatClient client, bool tools)
    {
        return new ChatSession((_, _) => Task.FromResult<IChatClient>(client))
        {
            Tools = tools ? new ToolSet([], new ToolContext(Path.GetTempPath())) : null,
        };
    }
}
