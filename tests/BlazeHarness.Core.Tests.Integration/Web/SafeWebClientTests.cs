using BlazeHarness.Core.Web;
using BlazeHarness.Core.Web.Content;
using BlazeHarness.Tests.Shared;
using BlazeHarness.Tests.Shared.Office;
using System.Globalization;
using System.Net;
using System.Web;

namespace BlazeHarness.Core.Tests.Integration.Web;

public sealed class SafeWebClientTests : IDisposable
{
    private readonly HttpListener _listener;
    private readonly string _prefix;
    private readonly CancellationTokenSource _stop = new();

    public SafeWebClientTests()
    {
        var (listener, port) = LocalHttpListener.Start("127.0.0.1");
        _listener = listener;
        _prefix = $"http://127.0.0.1:{port}/";
        _ = Task.Run(ServeAsync, _stop.Token);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Close();
        _stop.Dispose();
    }

    [Fact]
    public async Task FetchAsync_Loopback_RefusedByDefault()
    {
        // Arrange
        // Act
        var act = async () =>
        {
            using var sut = new SafeWebClient();
            return await sut.FetchAsync(new Uri(_prefix + "ok"), TestContext.Current.CancellationToken);
        };

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task FetchAsync_Page_ReturnsBodyAndType()
    {
        // Arrange
        using var sut = new SafeWebClient(_ => true);

        // Act
        var response = await sut.FetchAsync(new Uri(_prefix + "ok"), TestContext.Current.CancellationToken);

        // Assert
        response.Status.Should().Be(200);
        response.ContentType.Should().Be("text/html");
        System.Text.Encoding.UTF8.GetString(response.Body).Should().Contain("hello");
    }

    [Fact]
    public async Task FetchAsync_SameSiteRedirect_IsFollowed()
    {
        // Arrange
        using var sut = new SafeWebClient(_ => true);

        // Act
        var response = await sut.FetchAsync(new Uri(_prefix + "same"), TestContext.Current.CancellationToken);

        // Assert
        response.FinalUri.AbsolutePath.Should().Be("/ok");
    }

    [Fact]
    public async Task FetchAsync_CrossSiteRedirect_IsReturnedNotFollowed()
    {
        // Arrange
        using var sut = new SafeWebClient(_ => true);

        // Act
        var response = await sut.FetchAsync(new Uri(_prefix + "cross"), TestContext.Current.CancellationToken);

        // Assert
        response.RedirectTo.Should().Be(new Uri("http://localhost:1/elsewhere"));
    }

    [Fact]
    public async Task FetchAsync_OversizedPage_IsTruncated()
    {
        // Arrange
        using var sut = new SafeWebClient(_ => true);

        // Act
        var response = await sut.FetchAsync(new Uri(_prefix + "big"), TestContext.Current.CancellationToken);

        // Assert
        response.Truncated.Should().BeTrue();
        response.Body.Length.Should().Be(SafeWebClient.MaxPageBytes);
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    public async Task FetchAsync_DocumentOverPageLimit_IsNotTruncated(string contentType)
    {
        // Arrange
        using var sut = new SafeWebClient(_ => true);

        // Act
        var response = await sut.FetchAsync(new Uri($"{_prefix}doc?mb=12&type={Uri.EscapeDataString(contentType)}"), TestContext.Current.CancellationToken);

        // Assert
        response.Truncated.Should().BeFalse();
        response.Body.Length.Should().Be(12 * 1024 * 1024);
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    public async Task FetchAsync_OversizedDocument_IsTruncatedAtTheDocumentLimit(string contentType)
    {
        // Arrange
        using var sut = new SafeWebClient(_ => true);

        // Act
        var response = await sut.FetchAsync(new Uri($"{_prefix}doc?mb=34&type={Uri.EscapeDataString(contentType)}"), TestContext.Current.CancellationToken);

        // Assert
        response.Truncated.Should().BeTrue();
        response.Body.Length.Should().Be(SafeWebClient.MaxDocumentBytes);
    }

    [Fact]
    public async Task FetchAsync_Request_AsksForOfficeDocuments()
    {
        // Arrange
        using var sut = new SafeWebClient(_ => true);

        // Act
        var response = await sut.FetchAsync(new Uri(_prefix + "accept"), TestContext.Current.CancellationToken);

        // Assert
        var accept = System.Text.Encoding.UTF8.GetString(response.Body);
        accept.Should().Contain("application/pdf")
            .And.Contain("wordprocessingml.document")
            .And.Contain("spreadsheetml.sheet")
            .And.Contain("presentationml.presentation");
    }

    [Theory]
    [InlineData("docx")]
    [InlineData("docx-octet")]
    public async Task FetchAndExtract_WordDocument_ReturnsItsText(string route)
    {
        // Arrange
        using var sut = new SafeWebClient(_ => true);
        var extractor = new ContentExtractor(new PdfPigTextExtractor(), new OpenXmlTextExtractor());

        // Act
        var response = await sut.FetchAsync(new Uri(_prefix + route), TestContext.Current.CancellationToken);
        var document = extractor.Extract(response, TestContext.Current.CancellationToken);

        // Assert
        document.Text.Should().Contain("Served over HTTP");
    }

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            var response = context.Response;
            switch (context.Request.Url!.AbsolutePath)
            {
                case "/same":
                    response.StatusCode = 302;
                    response.RedirectLocation = "/ok";
                    break;
                case "/cross":
                    response.StatusCode = 302;
                    response.RedirectLocation = "http://localhost:1/elsewhere";
                    break;
                case "/big":
                    response.ContentType = "text/html";
                    await WriteChunksAsync(response, 12);
                    break;
                case "/doc":
                    var query = HttpUtility.ParseQueryString(context.Request.Url.Query);
                    response.ContentType = query["type"];
                    await WriteChunksAsync(response, int.Parse(query["mb"]!, CultureInfo.InvariantCulture));
                    break;
                case "/accept":
                    response.ContentType = "text/plain";
                    await response.OutputStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(context.Request.Headers["Accept"] ?? string.Empty), _stop.Token);
                    break;
                case "/docx":
                case "/docx-octet":
                    var docx = OfficeFiles.Word([OfficeFiles.Paragraph("Served over HTTP")]);
                    response.ContentType = context.Request.Url.AbsolutePath == "/docx"
                        ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                        : "application/octet-stream";
                    await response.OutputStream.WriteAsync(docx, _stop.Token);
                    break;
                default:
                    response.ContentType = "text/html";
                    await response.OutputStream.WriteAsync("<p>hello</p>"u8.ToArray(), _stop.Token);
                    break;
            }

            response.Close();
        }
    }

    private async Task WriteChunksAsync(HttpListenerResponse response, int megabytes)
    {
        var chunk = new byte[1024 * 1024];
        try
        {
            for (var i = 0; i < megabytes; i++)
            {
                await response.OutputStream.WriteAsync(chunk, _stop.Token);
            }
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException or OperationCanceledException)
        {
            // The client stops reading once the cap is reached.
        }
    }
}
