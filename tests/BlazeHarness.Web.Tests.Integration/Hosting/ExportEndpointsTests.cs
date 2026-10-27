using BlazeHarness.Core;
using BlazeHarness.Core.Sessions;
using BlazeHarness.Web.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazeHarness.Web.Tests.Integration.Hosting;

public sealed class ExportEndpointsTests : IDisposable
{
    private readonly TempFolder _temp = new("bh-export-endpoint-");
    private readonly SessionStore _store;
    private readonly SessionService _service;

    public ExportEndpointsTests()
    {
        _store = new SessionStore(new AppPaths(_temp.Root, _temp.Root), NullLoggerFactory.Instance);
        _service = new SessionService(_store, (_, _) => throw new NotSupportedException(), NullLoggerFactory.Instance);
    }

    [Theory]
    [InlineData("json", "application/json; charset=utf-8", ".json")]
    [InlineData("md", "text/markdown; charset=utf-8", ".md")]
    [InlineData(null, "text/markdown; charset=utf-8", ".md")]
    [InlineData("anything-else", "text/markdown; charset=utf-8", ".md")]
    public async Task ServeAsync_KnownSession_SendsTheFormatAsADownloadThatCannotBeSniffed(string? format, string contentType, string extension)
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
    public async Task ServeAsync_UnknownSession_IsNotFoundAndSetsNoDownloadHeaders()
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
    [InlineData(ExportFormat.Json, "api/sessions/abc/export?format=json")]
    [InlineData(ExportFormat.Markdown, "api/sessions/abc/export?format=md")]
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
        address.Should().Be("api/sessions/a%2Fb%3Fc/export?format=json");
    }

    public void Dispose()
    {
        _service.Dispose();
        _store.Dispose();
        _temp.Dispose();
    }
}
