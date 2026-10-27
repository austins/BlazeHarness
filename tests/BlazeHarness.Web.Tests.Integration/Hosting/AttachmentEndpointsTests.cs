using BlazeHarness.Core;
using BlazeHarness.Core.Sessions;
using BlazeHarness.Web.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazeHarness.Web.Tests.Integration.Hosting;

public sealed class AttachmentEndpointsTests : IDisposable
{
    private readonly TempFolder _temp = new("bh-endpoint-");
    private readonly SessionStore _store;
    private readonly SessionService _service;

    public AttachmentEndpointsTests()
    {
        _store = new SessionStore(new AppPaths(_temp.Root, _temp.Root), NullLoggerFactory.Instance);
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
    public async Task ServeFolderImage_ImageInTheSessionFolder_IsServedAndOtherFilesAreNot()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var folder = Path.Combine(_temp.Root, "work");
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, "a.png"), [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1], ct);
        await File.WriteAllTextAsync(Path.Combine(folder, "secret.txt"), "secret", ct);
        var info = await _service.CreateAsync(folder, ct);
        var context = new DefaultHttpContext();

        // Act
        var image = AttachmentEndpoints.ServeFolderImage(info.Id, "a.png", _service, context);
        var text = AttachmentEndpoints.ServeFolderImage(info.Id, "secret.txt", _service, new DefaultHttpContext());
        var escaped = AttachmentEndpoints.ServeFolderImage(info.Id, "../a.png", _service, new DefaultHttpContext());
        var none = AttachmentEndpoints.ServeFolderImage(info.Id, null, _service, new DefaultHttpContext());

        // Assert
        image.Should().BeOfType<PhysicalFileHttpResult>().Which.ContentType.Should().Be("image/png");
        context.Response.Headers.XContentTypeOptions.ToString().Should().Be("nosniff");
        text.Should().BeOfType<NotFound>();
        escaped.Should().BeOfType<NotFound>();
        none.Should().BeOfType<NotFound>();
    }

    [Fact]
    public void FileAddressOf_PathWithSpecialCharacters_IsEscaped()
    {
        // Act
        var address = AttachmentEndpoints.FileAddressOf("s1", "my dir/a&b.png");

        // Assert
        address.Should().Be("api/sessions/s1/files?path=my%20dir%2Fa%26b.png");
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
        _temp.Dispose();
    }
}
