using BlazeHarness.Core;
using BlazeHarness.Core.Sessions;
using BlazeHarness.Web.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazeHarness.Web.Tests.Unit.Hosting;

public sealed class AttachmentEndpointsTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "bh-endpoint-" + Guid.NewGuid().ToString("N"));
    private readonly SessionStore _store;
    private readonly SessionService _service;

    public AttachmentEndpointsTests()
    {
        _store = new SessionStore(new AppPaths(_home, _home), NullLoggerFactory.Instance);
        _service = new SessionService(_store, (_, _) => throw new NotSupportedException(), NullLoggerFactory.Instance);
    }

    [Fact]
    public async Task Serve_SavedTextFile_IsSentAsPlainTextThatCannotBeSniffedOrRun()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var info = await _service.CreateAsync(cancellationToken: ct);
        await using var content = new MemoryStream("<script>alert(1)</script>"u8.ToArray());
        var file = await _service.SaveAttachmentAsync(info.Id, "page.html", content, ct);
        var context = new DefaultHttpContext();

        // Act
        var result = AttachmentEndpoints.Serve(info.Id, file!.Id, _service, context);

        // Assert
        result.Should().BeOfType<PhysicalFileHttpResult>().Which.ContentType.Should().Be("text/plain; charset=utf-8");
        context.Response.Headers.XContentTypeOptions.ToString().Should().Be("nosniff");
        context.Response.Headers.ContentSecurityPolicy.ToString().Should().StartWith("sandbox");
    }

    [Fact]
    public void Serve_UnknownSession_IsNotFound()
    {
        // Act
        var result = AttachmentEndpoints.Serve("nope", "0123abcd", _service, new DefaultHttpContext());

        // Assert
        result.Should().BeOfType<NotFound>();
    }

    [Fact]
    public async Task Serve_UnknownOrMalformedAttachmentId_IsNotFound()
    {
        // Arrange
        var info = await _service.CreateAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var missing = AttachmentEndpoints.Serve(info.Id, "0123abcd", _service, new DefaultHttpContext());
        var traversal = AttachmentEndpoints.Serve(info.Id, "..", _service, new DefaultHttpContext());

        // Assert
        missing.Should().BeOfType<NotFound>();
        traversal.Should().BeOfType<NotFound>();
    }

    [Fact]
    public void AddressOf_IdsWithSpecialCharacters_AreEscaped()
    {
        // Act
        var address = AttachmentEndpoints.AddressOf("a/b", "c d");

        // Assert
        address.Should().Be("api/sessions/a%2Fb/attachments/c%20d");
    }

    public void Dispose()
    {
        _service.Dispose();
        _store.Dispose();
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }
}
