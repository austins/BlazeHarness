using BlazeHarness.Core.Providers;
using System.Net;

namespace BlazeHarness.Core.Tests.Unit.Providers;

public sealed class ProviderConnectionTesterTests
{
    private static readonly ProviderConfig Provider = new() { Id = "p", DisplayName = "P", BaseUrl = "http://host/v1/" };

    [Fact]
    public async Task TestAsync_ModelList_ReturnsSortedIds_AndSendsBearerKey()
    {
        // Arrange
        using var handler = new StubHandler(_ => Json("""{"data":[{"id":"b"},{"id":"A"},{"object":"x"}]}"""));
        using var client = new HttpClient(handler);

        // Act
        var result = await ProviderConnectionTester.TestAsync(Provider, "k", client, TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.ModelIds.Should().Equal("A", "b");
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.ToString().Should().Be("http://host/v1/models");
        handler.Requests[0].Headers.Authorization!.ToString().Should().Be("Bearer k");
    }

    [Fact]
    public async Task TestAsync_NoKey_SendsNoAuthorization()
    {
        // Arrange
        using var handler = new StubHandler(_ => Json("""{"data":[]}"""));
        using var client = new HttpClient(handler);

        // Act
        var result = await ProviderConnectionTester.TestAsync(Provider, null, client, TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Message.Should().Contain("no models");
        handler.Requests[0].Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task TestAsync_ExtraHeaders_AreSent()
    {
        // Arrange
        using var handler = new StubHandler(_ => Json("""{"data":[]}"""));
        using var client = new HttpClient(handler);
        var provider = Provider with { Headers = new Dictionary<string, string> { ["X-Org"] = "acme" } };

        // Act
        await ProviderConnectionTester.TestAsync(provider, null, client, TestContext.Current.CancellationToken);

        // Assert
        handler.Requests[0].Headers.GetValues("X-Org").Should().Equal("acme");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "rejected the API key")]
    [InlineData(HttpStatusCode.Forbidden, "rejected the API key")]
    [InlineData(HttpStatusCode.NotFound, "/v1")]
    [InlineData(HttpStatusCode.InternalServerError, "500")]
    public async Task TestAsync_ErrorStatus_ExplainsHowToFix(HttpStatusCode status, string expected)
    {
        // Arrange
        using var handler = new StubHandler(_ => new HttpResponseMessage(status));
        using var client = new HttpClient(handler);

        // Act
        var result = await ProviderConnectionTester.TestAsync(Provider, "k", client, TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain(expected);
    }

    [Fact]
    public async Task TestAsync_NotJson_ExplainsExpectedFormat()
    {
        // Arrange
        using var handler = new StubHandler(_ => Json("<html>hi</html>"));
        using var client = new HttpClient(handler);

        // Act
        var result = await ProviderConnectionTester.TestAsync(Provider, "k", client, TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("model list");
    }

    [Fact]
    public async Task TestAsync_ConnectionFailure_ReturnsFailedResult()
    {
        // Arrange
        using var handler = new StubHandler(_ => throw new HttpRequestException("refused"));
        using var client = new HttpClient(handler);

        // Act
        var result = await ProviderConnectionTester.TestAsync(Provider, "k", client, TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("refused");
    }

    [Fact]
    public async Task TestAsync_AuthorizationHeaderConfigured_KeyReplacesIt()
    {
        // Arrange
        using var handler = new StubHandler(_ => Json("""{"data":[]}"""));
        using var client = new HttpClient(handler);
        var provider = Provider with { Headers = new Dictionary<string, string> { ["Authorization"] = "Basic abc" } };

        // Act
        await ProviderConnectionTester.TestAsync(provider, "k", client, TestContext.Current.CancellationToken);

        // Assert
        handler.Requests[0].Headers.GetValues("Authorization").Should().ContainSingle().Which.Should().Be("Bearer k");
    }

    [Fact]
    public async Task TestAsync_OversizedBody_FailsWithoutReadingItAll()
    {
        // Arrange
        using var handler = new StubHandler(_ => Json(new string('x', 5 * 1024 * 1024)));
        using var client = new HttpClient(handler);

        // Act
        var result = await ProviderConnectionTester.TestAsync(Provider, "k", client, TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("too large");
    }

    [Fact]
    public async Task TestAsync_InvalidBaseUrl_FailsWithoutRequest()
    {
        // Arrange
        using var handler = new StubHandler(_ => Json("{}"));
        using var client = new HttpClient(handler);

        // Act
        var result = await ProviderConnectionTester.TestAsync(Provider with { BaseUrl = "nope" }, "k", client, TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeFalse();
        handler.Requests.Should().BeEmpty();
    }

    private static HttpResponseMessage Json(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }
}
