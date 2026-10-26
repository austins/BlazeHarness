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
    public void Build_Default_HasNoVolatileContent()
    {
        // Act
        var prompt = SystemPrompt.Build("Linux", "bash", "/work");

        // Assert
        prompt.Should().NotContainEquivalentOf(DateTime.UtcNow.Year.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
