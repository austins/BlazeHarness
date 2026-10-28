using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;

namespace BlazeHarness.Tests.Shared.Providers;

/// <summary>A tiny in-process OpenAI-compatible chat completions endpoint that replays canned SSE chunks.</summary>
/// <remarks>It listens on a port the operating system picks, so parallel tests never collide.</remarks>
public sealed class FakeOpenAiServer : IDisposable
{
    private readonly WebApplication _app;
    private readonly Func<int, string, string[]?> _chunksForRequest;
    private readonly Func<int, HttpStatusCode> _statusForRequest;
    private readonly Lock _gate = new();
    private readonly List<string> _requestBodies = [];
    private int _requestCount;

    /// <summary>Initializes a new instance of the <see cref="FakeOpenAiServer"/> class that answers every request with the same chunks.</summary>
    /// <param name="sseChunks">The chunks to stream, without the <c>data:</c> prefix or the closing <c>[DONE]</c>.</param>
    public FakeOpenAiServer(string[] sseChunks)
        : this(_ => sseChunks, _ => HttpStatusCode.OK)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="FakeOpenAiServer"/> class that picks its reply by request number.</summary>
    /// <param name="chunksForRequest">Returns the chunks to stream for a request, counting from 1; null streams none.</param>
    /// <param name="statusForRequest">Returns the status code for a request, counting from 1. Anything but OK sends no body.</param>
    public FakeOpenAiServer(Func<int, string[]?> chunksForRequest, Func<int, HttpStatusCode> statusForRequest)
        : this((index, _) => chunksForRequest(index), statusForRequest)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="FakeOpenAiServer"/> class that picks its reply from the request body.</summary>
    /// <param name="chunksForBody">Returns the chunks to stream for a request body, so a script does not depend on request order.</param>
    public FakeOpenAiServer(Func<string, string[]?> chunksForBody)
        : this((_, body) => chunksForBody(body), _ => HttpStatusCode.OK)
    {
    }

    private FakeOpenAiServer(Func<int, string, string[]?> chunksForRequest, Func<int, HttpStatusCode> statusForRequest)
    {
        _chunksForRequest = chunksForRequest;
        _statusForRequest = statusForRequest;

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();
        _app.Run(HandleAsync);
        _app.Start();

        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        BaseUrl = new Uri($"{address}/v1");
    }

    /// <summary>Gets the base URL to give a provider, ending in <c>/v1</c>.</summary>
    public Uri BaseUrl { get; }

    /// <summary>Gets a copy of the bodies of the requests received so far.</summary>
    public IReadOnlyList<string> RequestBodies
    {
        get
        {
            lock (_gate)
            {
                return [.. _requestBodies];
            }
        }
    }

    /// <summary>Gets the number of requests received so far.</summary>
    public int RequestCount => Volatile.Read(ref _requestCount);

    /// <inheritdoc />
    public void Dispose()
    {
#pragma warning disable VSTHRD002 // Dispose can't be asynchronous here.
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
    }

    private async Task HandleAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(context.RequestAborted);
        lock (_gate)
        {
            _requestBodies.Add(body);
        }

        var index = Interlocked.Increment(ref _requestCount);
        var status = _statusForRequest(index);
        context.Response.StatusCode = (int)status;
        if (status != HttpStatusCode.OK)
        {
            return;
        }

        context.Response.ContentType = "text/event-stream";
        foreach (var chunk in _chunksForRequest(index, body) ?? [])
        {
            await context.Response.WriteAsync($"data: {chunk}\n\n", context.RequestAborted);
        }

        await context.Response.WriteAsync("data: [DONE]\n\n", context.RequestAborted);
    }
}
