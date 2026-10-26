using System.Net;
using System.Text;

namespace BlazeHarness.Core.Tests.Integration;

/// <summary>A tiny in-process OpenAI-compatible chat completions endpoint that replays canned SSE chunks.</summary>
internal sealed class FakeOpenAiServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Func<int, string[]?> _chunksForRequest;
    private readonly Func<int, HttpStatusCode> _statusForRequest;
    private readonly Task _loop;
    private int _requestCount;

    public FakeOpenAiServer(string[] sseChunks)
        : this(_ => sseChunks, _ => HttpStatusCode.OK)
    {
    }

    public FakeOpenAiServer(Func<int, string[]?> chunksForRequest, Func<int, HttpStatusCode> statusForRequest)
    {
        _chunksForRequest = chunksForRequest;
        _statusForRequest = statusForRequest;

        var port = GetFreePort();
        BaseUrl = $"http://127.0.0.1:{port}/v1";
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _loop = Task.Run(Loop);
    }

    public string BaseUrl { get; }

    public List<string> RequestBodies { get; } = [];

    public int RequestCount => Volatile.Read(ref _requestCount);

    public void Dispose()
    {
        _listener.Close();
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // The listener was closed while waiting for a request.
        }
    }

    private static int GetFreePort()
    {
        using var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private async Task Loop()
    {
        while (_listener.IsListening)
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

            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            lock (RequestBodies)
            {
                RequestBodies.Add(body);
            }

            var index = Interlocked.Increment(ref _requestCount);
            var status = _statusForRequest(index);
            context.Response.StatusCode = (int)status;
            if (status != HttpStatusCode.OK)
            {
                context.Response.Close();
                continue;
            }

            context.Response.ContentType = "text/event-stream";
            var chunks = _chunksForRequest(index) ?? [];
            await using var writer = new StreamWriter(context.Response.OutputStream, new UTF8Encoding(false));
            foreach (var chunk in chunks)
            {
                await writer.WriteAsync($"data: {chunk}\n\n");
            }

            await writer.WriteAsync("data: [DONE]\n\n");
            await writer.FlushAsync();
            context.Response.Close();
        }
    }
}
