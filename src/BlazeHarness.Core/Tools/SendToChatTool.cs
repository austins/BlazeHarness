using System.Text.Json;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// Lets the model send a message to another chat and start a turn there, for example to pass what it finished to a chat that depends on it.
/// </summary>
/// <remarks>
/// The call needs approval from the permission check (see <see cref="ISendToChatTool"/>), and the receiving chat keeps its own permissions.
/// The result only says the message was accepted: there is no reply to the sending chat.
/// </remarks>
internal sealed class SendToChatTool : ISendToChatTool
{
    /// <summary>
    /// The name of the tool.
    /// </summary>
    public const string ToolName = "send_to_chat";

    /// <summary>
    /// The value of the <c>chat</c> argument that creates a chat.
    /// </summary>
    public const string NewChat = "new";

    /// <summary>
    /// The value of the <c>folder</c> argument that creates a chat without a folder.
    /// </summary>
    public const string NoFolder = "none";

    private const string ChatArgument = "chat";
    private const string MessageArgument = "message";
    private const string FolderArgument = "folder";

    private readonly Func<string, string?, string, SendToChatResolution> _resolve;
    private readonly Func<SendToChatPlan, CancellationToken, Task<ToolResult>> _deliver;

    /// <summary>
    /// Initializes a new instance of the <see cref="SendToChatTool"/> class.
    /// </summary>
    /// <param name="resolve">Finds the receiving chat from the <c>chat</c> and <c>folder</c> arguments and the message.</param>
    /// <param name="deliver">Creates the chat if needed and starts a turn there or queues the message, once the call is approved.</param>
    public SendToChatTool(Func<string, string?, string, SendToChatResolution> resolve, Func<SendToChatPlan, CancellationToken, Task<ToolResult>> deliver)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        ArgumentNullException.ThrowIfNull(deliver);
        _resolve = resolve;
        _deliver = deliver;
    }

    /// <inheritdoc />
    public string Name => ToolName;

    /// <inheritdoc />
    public string Description =>
        "Send a message to another chat and start a turn there, for example to pass what you finished to a chat that depends on it. " +
        "The other chat can't see this conversation, so the message must hold everything it needs: what was decided or built, the paths involved, and what to do next. " +
        $"Set '{ChatArgument}' to a reference like chat:<id> or an id the user gave you, the exact title of a chat, or '{NewChat}' to start a chat. " +
        $"With '{NewChat}', '{FolderArgument}' is a folder that is open in the app, or '{NoFolder}' for a chat without a folder; leave it out to use this chat's own. " +
        "The other chat keeps its own permissions, and a message to a chat in another folder always asks the user. " +
        "You get no reply: say in your answer where you sent it. If the chat is busy, the message waits until its reply ends.";

    /// <inheritdoc />
    public string InputSchema => $$"""
        {
          "type": "object",
          "properties": {
            "{{ChatArgument}}": { "type": "string", "description": "A chat reference (chat:<id>), a chat id, the exact title of a chat, or '{{NewChat}}'." },
            "{{MessageArgument}}": { "type": "string", "maxLength": {{SendToChatPlan.MaxMessageLength}}, "description": "The self-contained message the other chat starts from." },
            "{{FolderArgument}}": { "type": "string", "description": "Only with '{{NewChat}}': an open folder's path, or '{{NoFolder}}'. Defaults to this chat's folder." }
          },
          "required": ["{{ChatArgument}}", "{{MessageArgument}}"]
        }
        """;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public TimeSpan Timeout { get; } = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public string Summarize(JsonElement arguments) => ToolSupport.Text(arguments, ChatArgument);

    /// <inheritdoc />
    public SendToChatResolution Plan(JsonElement arguments) => Resolve(new ToolArguments(arguments));

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var resolution = Resolve(arguments);
        return resolution.Plan is { } plan
            ? await _deliver(plan, cancellationToken)
            : ToolResult.Error(resolution.Error ?? "That chat can't be used.");
    }

    private SendToChatResolution Resolve(ToolArguments read)
    {
        try
        {
            var chat = read.RequiredString(ChatArgument);
            var message = read.RequiredString(MessageArgument);
            return Check(chat, message) is { } problem
                ? new SendToChatResolution(null, problem)
                : _resolve(chat, read.OptionalString(FolderArgument), message);
        }
        catch (ToolArgumentException ex)
        {
            return new SendToChatResolution(null, ex.Message);
        }
    }

    private static string? Check(string chat, string message)
    {
        if (string.IsNullOrWhiteSpace(chat))
        {
            return $"The '{ChatArgument}' argument must name a chat.";
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return $"The '{MessageArgument}' argument must not be empty.";
        }

        return message.Length > SendToChatPlan.MaxMessageLength
            ? $"The message is {message.Length} characters; shorten it to at most {SendToChatPlan.MaxMessageLength} so the user can read all of it."
            : null;
    }
}
