namespace BlazeHarness.Core.Web;

/// <summary>
/// A search provider's place in the saved order, and whether it is used.
/// </summary>
/// <param name="Id">The provider id, one of <see cref="WebProviders.All"/>.</param>
/// <param name="Enabled">Whether searches use the provider.</param>
public sealed record WebProviderSetting(string Id, bool Enabled = true);
