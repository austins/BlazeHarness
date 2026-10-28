using BlazeHarness.Core.Web;
using BlazeHarness.Core.Web.Content;
using Microsoft.Extensions.Time.Testing;

namespace BlazeHarness.Core.Tests.Unit.Web;

public sealed class WebCacheTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly WebCache _sut;

    public WebCacheTests()
    {
        _sut = new WebCache(_time);
    }

    [Fact]
    public void Find_AfterAdd_ReturnsPageOnlyForSameSession()
    {
        // Arrange
        var address = new Uri("https://example.com/a");
        _sut.Add("one", address, Page(address));

        // Act / Assert
        _sut.Find("one", address).Should().NotBeNull();
        _sut.Find("two", address).Should().BeNull();
    }

    [Fact]
    public void Find_AfterLifetime_ReturnsNull()
    {
        // Arrange
        var address = new Uri("https://example.com/a");
        _sut.Add("one", address, Page(address));

        // Act
        _time.Advance(WebCache.Lifetime + TimeSpan.FromSeconds(1));

        // Assert
        _sut.Find("one", address).Should().BeNull();
    }

    [Fact]
    public void Add_PastPageLimit_DropsOldest()
    {
        // Arrange
        for (var i = 0; i <= WebCache.MaxPagesPerSession; i++)
        {
            var address = new Uri($"https://example.com/{i}");
            _sut.Add("one", address, Page(address));
        }

        // Act / Assert
        _sut.Find("one", new Uri("https://example.com/0")).Should().BeNull();
        _sut.Find("one", new Uri($"https://example.com/{WebCache.MaxPagesPerSession}")).Should().NotBeNull();
    }

    [Fact]
    public void Forget_RemovesSessionPages()
    {
        // Arrange
        var address = new Uri("https://example.com/a");
        _sut.Add("one", address, Page(address));

        // Act
        _sut.Forget("one");

        // Assert
        _sut.Find("one", address).Should().BeNull();
    }

    private static CachedPage Page(Uri address) => new(address, new ExtractedDocument("t", "text", []));
}
