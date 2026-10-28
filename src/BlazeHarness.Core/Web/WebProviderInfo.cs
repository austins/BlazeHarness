namespace BlazeHarness.Core.Web;

/// <summary>
/// What the settings page shows about a search provider.
/// </summary>
/// <param name="Id">The id saved in the settings.</param>
/// <param name="Name">The name shown to the user.</param>
/// <param name="Description">One line on how the provider is reached.</param>
public sealed record WebProviderInfo(string Id, string Name, string Description);
