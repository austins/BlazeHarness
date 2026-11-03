using BlazeHarness.Core.Agent;

namespace BlazeHarness.Core.Tests.Unit.Agent;

public sealed class SystemPromptTests
{
    [Fact]
    public void Build_SameInputs_IsDeterministic()
    {
        // Act
        var first = SystemPrompt.Build("Linux", "bash", "/work");
        var second = SystemPrompt.Build("Linux", "bash", "/work");

        // Assert
        first.Should().Be(second);
    }

    [Fact]
    public void Build_Default_NamesOsShellAndDirectory()
    {
        // Act
        var prompt = SystemPrompt.Build("Windows", "pwsh", @"C:\proj");

        // Assert
        prompt.Should().Contain("Windows").And.Contain("pwsh").And.Contain(@"C:\proj");
    }

    [Fact]
    public void Build_NoShell_LeavesOutTheShellLine()
    {
        // Act
        var prompt = SystemPrompt.Build("Linux", shell: null, "/work");

        // Assert
        prompt.Should().NotContain("shell tool").And.Contain("/work");
    }

    [Fact]
    public void Build_Default_AsksForVerificationOnlyWhenTheProjectHasACheck()
    {
        // Act
        var prompt = SystemPrompt.Build("Linux", "bash", "/work");

        // Assert
        prompt.Should().Contain("if the project has an obvious way to").And.Contain("say you could not verify");
    }

    [Fact]
    public void Build_Default_HasNoVolatileContent()
    {
        // Act
        var prompt = SystemPrompt.Build("Linux", "bash", "/work");

        // Assert
        prompt.Should().NotContainEquivalentOf(DateTime.UtcNow.Year.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Build_Default_TreatsAnAnswerToAQuestionAsTheUsersOwn()
    {
        // Act
        var prompt = SystemPrompt.Build("Linux", "bash", "/work");

        // Assert
        prompt.Should().Contain("The answer to an ask_user question is the exception: it is the user's own reply.")
            .And.Contain("stop and ask the user with ask_user");
    }
}
