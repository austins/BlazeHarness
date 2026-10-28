using BlazeHarness.Core.Web;
using System.Net;
using System.Net.Sockets;

namespace BlazeHarness.Core.Tests.Integration.Web;

public sealed class SafeWebClientTests : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly string _prefix;
    private readonly CancellationTokenSource _stop = new();

    public SafeWebClientTests()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        _prefix = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(_prefix);
        _listener.Start();
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
                    var chunk = new byte[1024 * 1024];
                    try
                    {
                        for (var i = 0; i < 12; i++)
                        {
                            await response.OutputStream.WriteAsync(chunk, _stop.Token);
                        }
                    }
                    catch (Exception ex) when (ex is HttpListenerException or IOException or OperationCanceledException)
                    {
                        // The client stops reading once the cap is reached.
                    }

                    break;
                default:
                    response.ContentType = "text/html";
                    await response.OutputStream.WriteAsync("<p>hello</p>"u8.ToArray(), _stop.Token);
                    break;
            }

            response.Close();
        }
    }
}
