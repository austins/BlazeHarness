using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Providers;

namespace BlazeHarness.Tests.Shared.Agent;

/// <summary>Builds chat targets for tests.</summary>
public static class TestTargets
{
    public static ChatTarget Create(string provider = "p1", string model = "fast", ThinkingLevel? level = null, InputTypes input = InputTypes.Text, int contextWindow = 8000)
    {
        var modelConfig = new ModelConfig { Id = model, ContextWindow = contextWindow, MaxOutputTokens = 1000, Input = input };
        var providerConfig = new ProviderConfig
        {
            Id = provider,
            DisplayName = provider.ToUpperInvariant(),
            BaseUrl = new Uri("http://localhost/v1"),
            Models = [modelConfig],
        };
        return new ChatTarget(providerConfig, modelConfig, level);
    }
}
