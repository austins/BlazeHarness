using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Providers;

namespace BlazeHarness.Core.Tests.Unit.Agent;

/// <summary>Builds chat targets for tests.</summary>
internal static class TestTargets
{
    public static ChatTarget Create(string provider = "p1", string model = "fast", ThinkingLevel? level = null, InputTypes input = InputTypes.Text)
    {
        var modelConfig = new ModelConfig { Id = model, ContextWindow = 8000, MaxOutputTokens = 1000, Input = input };
        var providerConfig = new ProviderConfig
        {
            Id = provider,
            DisplayName = provider.ToUpperInvariant(),
            BaseUrl = "http://localhost/v1",
            Models = [modelConfig],
        };
        return new ChatTarget(providerConfig, modelConfig, level);
    }
}
