using BlazeHarness.Core;
using BlazeHarness.Core.Chats;
using BlazeHarness.Core.Chats.Export;
using BlazeHarness.Tests.Shared;
using BlazeHarness.Web.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazeHarness.Web.Tests.Integration.Hosting;

public sealed class ExportEndpointsTests : IDisposable
{
    private readonly TempFolder _temp = new("bh-export-endpoint-");
    private readonly ChatStore _store;
    private readonly ChatService _service;

    public ExportEndpointsTests()
    {
        _store = new ChatStore(new AppPaths(_temp.Root, _temp.Root), NullLoggerFactory.Instance, TimeProvider.System);
        _service = new ChatService(_store, (_, _) => throw new NotSupportedException(), NullLoggerFactory.Instance, TimeProvider.System);
    }

    [Theory]
    [InlineData("json", "application/json; charset=utf-8", ".json")]
    [InlineData("md", "text/markdown; charset=utf-8", ".md")]
    [InlineData(null, "text/markdown; charset=utf-8", ".md")]
    [InlineData("anything-else", "text/markdown; charset=utf-8", ".md")]
    public async Task ServeAsync_KnownChat_SendsTheFormatAsADownloadThatCannotBeSniffed(string? format, string contentType, string extension)
    {
        // Arrange
        var info = await _service.CreateAsync(cancellationToken: TestContext.Current.CancellationToken);
        var context = new DefaultHttpContext();

        // Act
        var result = await ExportEndpoints.ServeAsync(info.Id, format, _service, context, TestContext.Current.CancellationToken);

        // Assert
        var file = result.Should().BeOfType<FileContentHttpResult>().Subject;
        file.ContentType.Should().Be(contentType);
        file.FileDownloadName.Should().EndWith(extension);
        file.FileContents.Length.Should().BePositive();
        context.Response.Headers.XContentTypeOptions.ToString().Should().Be("nosniff");
    }

    [Fact]
    public async Task ServeAsync_UnknownChat_IsNotFoundAndSetsNoDownloadHeaders()
    {
        // Arrange
        var context = new DefaultHttpContext();

        // Act
        var result = await ExportEndpoints.ServeAsync("nope", "json", _service, context, TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeOfType<NotFound>();
        context.Response.Headers.XContentTypeOptions.ToString().Should().BeEmpty();
    }

    [Theory]
    [InlineData(ExportFormat.Json, "api/chats/abc/export?format=json")]
    [InlineData(ExportFormat.Markdown, "api/chats/abc/export?format=md")]
    public void AddressOf_Format_ProducesARelativeLinkTheRouteAccepts(ExportFormat format, string expected)
    {
        // Act
        var address = ExportEndpoints.AddressOf("abc", format);

        // Assert
        address.Should().Be(expected);
    }

    [Fact]
    public void AddressOf_IdWithSpecialCharacters_IsEscaped()
    {
        // Act
        var address = ExportEndpoints.AddressOf("a/b?c", ExportFormat.Json);

        // Assert
        address.Should().Be("api/chats/a%2Fb%3Fc/export?format=json");
    }

    public void Dispose()
    {
        _service.Dispose();
        _store.Dispose();
        _temp.Dispose();
    }
}
