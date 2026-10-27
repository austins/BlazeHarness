using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Globalization;

namespace BlazeHarness.Core.Mcp;

/// <summary>
/// Connects to MCP servers with the official SDK over stdio, streamable HTTP or SSE.
/// </summary>
/// <remarks>
/// A stdio server gets only the SDK's minimal baseline environment (such as <c>PATH</c> and the home folder)
/// plus the variables written in <c>mcp.json</c>, never the app's own environment, which can hold provider keys.
/// </remarks>
public sealed partial class SdkMcpConnector : IMcpConnector
{
    private const int StderrLinesKept = 8;

    // Shorter values would blank out ordinary words in the output.
    private const int MinRedactedLength = 4;

    private readonly ILoggerFactory _loggers;
    private readonly ILogger _log;

    /// <summary>
    /// Initializes a new instance of the <see cref="SdkMcpConnector"/> class.
    /// </summary>
    /// <param name="loggers">Creates the loggers for the connection.</param>
    public SdkMcpConnector(ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(loggers);
        _loggers = loggers;
        _log = loggers.CreateLogger<SdkMcpConnector>();
    }

    /// <inheritdoc />
    public async Task<IMcpConnection> ConnectAsync(McpServerConfig config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errorLines = new Queue<string>();
        IClientTransport transport = config.Transport == McpTransport.Stdio
            ? StdioTransport(config, errorLines)
            : HttpTransport(config);
        try
        {
            var options = new McpClientOptions { ClientInfo = new Implementation { Name = "BlazeHarness", Version = "1.0" } };
            var client = await McpClient.CreateAsync(transport, options, _loggers, cancellationToken).ConfigureAwait(false);
            return new SdkMcpConnection(client);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            LogConnectFailed(ex, config.Name);
            string tail;
            lock (errorLines)
            {
                tail = string.Join('\n', errorLines);
            }

            tail = Redact(tail, config);
            var message = tail.Length == 0 ? ex.Message : string.Create(CultureInfo.InvariantCulture, $"{ex.Message}\n{tail}");
            throw new McpConnectException(message, ex);
        }
    }

    /// <summary>
    /// Hide the values of a server's settings in text it printed, since a server may echo its own secrets.
    /// </summary>
    /// <param name="text">The text from the server.</param>
    /// <param name="config">The server whose environment and header values are hidden.</param>
    /// <returns>The text with each such value replaced.</returns>
    internal static string Redact(string text, McpServerConfig config)
    {
        foreach (var value in config.Env.Values.Concat(config.Headers.Values))
        {
            if (value.Length >= MinRedactedLength)
            {
                text = text.Replace(value, "***", StringComparison.Ordinal);
            }
        }

        return text;
    }

    private static StdioClientTransport StdioTransport(McpServerConfig config, Queue<string> errorLines)
    {
        var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        foreach (var (key, value) in config.Env)
        {
            environment[key] = value;
        }

        return new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = config.Name,
            Command = config.Command ?? string.Empty,
            Arguments = [.. config.Args],
            WorkingDirectory = config.Cwd,
            InheritEnvironmentVariables = false,
            EnvironmentVariables = environment,
            StandardErrorLines = line =>
            {
                lock (errorLines)
                {
                    errorLines.Enqueue(line);
                    while (errorLines.Count > StderrLinesKept)
                    {
                        errorLines.Dequeue();
                    }
                }
            },
        });
    }

    private HttpClientTransport HttpTransport(McpServerConfig config)
    {
        return new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Name = config.Name,
                Endpoint = new Uri(config.Url ?? string.Empty),
                AdditionalHeaders = config.Headers.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase),
                TransportMode = config.Transport == McpTransport.Sse ? HttpTransportMode.Sse : HttpTransportMode.AutoDetect,
            },
            _loggers);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not connect to the MCP server {Server}.")]
    private partial void LogConnectFailed(Exception ex, string server);
}
