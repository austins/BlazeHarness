using BlazeHarness.Core.Web;

namespace BlazeHarness.Core.Tests.Unit.Web;

public sealed class UrlCanonicalizerTests
{
    [Fact]
    public void Canonical_TrackingAndFragmentAndWww_Removed()
    {
        // Act
        var result = UrlCanonicalizer.Canonical("https://www.Example.com/a/?utm_source=x&id=2#top");

        // Assert
        result.Should().Be(UrlCanonicalizer.Canonical("https://example.com/a?id=2"));
    }
}
