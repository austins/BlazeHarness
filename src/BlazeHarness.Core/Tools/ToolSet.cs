using Microsoft.Extensions.AI;
using System.Globalization;
using System.Text.Json;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// The tools one session offers the model, and the code that runs a call safely.
/// </summary>
public sealed class ToolSet
{
    /// <summary>
    /// The most characters a call's output may have before it is cut, so one call can't fill the context.
    /// </summary>
    public const int MaxOutputCharacters = 30_000;

    private readonly Dictionary<string, ITool> _byName;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolSet"/> class.
    /// </summary>
    /// <param name="tools">The tools to offer; names must be unique.</param>
    /// <param name="context">What the calls run against.</param>
    /// <exception cref="ArgumentException">Two tools share a name.</exception>
    public ToolSet(IEnumerable<ITool> tools, ToolContext context)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(context);

        Context = context;
        Tools = [.. tools];
        if (Tools.GroupBy(t => t.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } duplicate)
        {
            throw new ArgumentException($"Two tools are named '{duplicate.Key}'.", nameof(tools));
        }

        _byName = Tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
        Declarations = [.. Tools.Select(t => AIFunctionFactory.CreateDeclaration(t.Name, t.Description, JsonDocument.Parse(t.InputSchema).RootElement.Clone()))];
    }

    /// <summary>
    /// Gets what the calls run against.
    /// </summary>
    public ToolContext Context { get; }

    /// <summary>
    /// Gets the tools in the order they are offered.
    /// </summary>
    public IReadOnlyList<ITool> Tools { get; }

    /// <summary>
    /// Gets the tool definitions to put in a chat request.
    /// </summary>
    public IReadOnlyList<AITool> Declarations { get; }

    /// <summary>
    /// Cut text to at most <see cref="MaxOutputCharacters"/> characters, saying how much was left out.
    /// </summary>
    /// <param name="output">The text to limit.</param>
    /// <returns>The text unchanged when it fits, otherwise its start followed by a note.</returns>
    public static string Limit(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (output.Length <= MaxOutputCharacters)
        {
            return output;
        }

        var keep = MaxOutputCharacters;
        if (char.IsHighSurrogate(output[keep - 1]))
        {
            keep--;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{output[..keep]}\n[Output cut: {output.Length - keep} more characters were left out.]");
    }

    /// <summary>
    /// Find a tool by name.
    /// </summary>
    /// <param name="name">The name from a call.</param>
    /// <returns>The tool, or null when there is none with that name.</returns>
    public ITool? Find(string name) => _byName.GetValueOrDefault(name);

    /// <summary>
    /// Describe a call in a few words for the UI.
    /// </summary>
    /// <param name="tool">The tool being called.</param>
    /// <param name="argumentsJson">The call arguments as JSON text.</param>
    /// <returns>The summary, or an empty string when the arguments can't be read.</returns>
    public static string Summarize(ITool tool, string argumentsJson)
    {
        ArgumentNullException.ThrowIfNull(tool);
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            return tool.Summarize(document.RootElement);
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Run one call, turning every failure the model can react to into an error result.
    /// </summary>
    /// <remarks>
    /// The call stops after the tool's timeout, and its output is cut at <see cref="MaxOutputCharacters"/>.
    /// </remarks>
    /// <param name="tool">The tool to run.</param>
    /// <param name="argumentsJson">The call arguments as JSON text.</param>
    /// <param name="cancellationToken">A token that stops the call.</param>
    /// <returns>The result to send back to the model.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<ToolResult> ExecuteAsync(ITool tool, string argumentsJson, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tool);

        using var timeout = new CancellationTokenSource(tool.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var result = await tool.ExecuteAsync(ToolArguments.Parse(argumentsJson), Context, linked.Token);
            return result with { Output = Limit(result.Output) };
        }
        catch (ToolArgumentException ex)
        {
            return ToolResult.Error(ex.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ToolResult.Error(string.Create(CultureInfo.InvariantCulture, $"The call took longer than {tool.Timeout.TotalSeconds:0} seconds and was stopped."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ToolResult.Error(ex.Message);
        }
    }
}
