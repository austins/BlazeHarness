using BlazeHarness.Core.Agent.Questions;
using System.Globalization;
using System.Text.Json;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// Lets the model ask the user one question and wait for the answer.
/// </summary>
/// <remarks>
/// <para>
/// The agent loop handles the call itself, like an approval, so it never goes through <see cref="ToolSet.ExecuteAsync"/> (no tool timeout and no parallel batch).
/// The tool is read-only with no path arguments, so the permission check allows it at every access level, and nothing reads the answer for a permission decision.
/// </para>
/// <para>
/// Compaction must never replace the result of an <c>ask_user</c> call with a stub: the answer is the user's own words. Check <see cref="UserQuestion.ToolName"/>.
/// </para>
/// </remarks>
internal sealed class AskUserTool : ITool
{
    /// <summary>
    /// The most questions one run may ask.
    /// </summary>
    public const int MaxPerRun = 3;

    /// <summary>
    /// The result of an <c>ask_user</c> call after the first in one response.
    /// </summary>
    public const string OneAtATime = "Ask one question at a time; ask this again after reading the answer.";

    /// <summary>
    /// The result of an <c>ask_user</c> call when nobody can be asked.
    /// </summary>
    public const string NoAsker = "This question was not asked because there is no way to ask the user.";

    /// <summary>
    /// The result of an <c>ask_user</c> call when the run has asked <see cref="MaxPerRun"/> questions already.
    /// </summary>
    public static readonly string TooMany = string.Create(CultureInfo.InvariantCulture, $"You've asked {MaxPerRun} questions in this run. Finish with your best judgment and list any open questions in your reply.");

    private const string QuestionArgument = "question";
    private const string OptionsArgument = "options";
    private const string MultiSelectArgument = "multi_select";
    private const string LabelArgument = "label";
    private const string DescriptionArgument = "description";
    private const int SummaryLength = 100;

    /// <inheritdoc />
    public string Name => UserQuestion.ToolName;

    /// <inheritdoc />
    public string Description =>
        "Ask the user one question and wait for the answer. Use it only when you can't reasonably continue without them: the request is ambiguous in a way that changes the result, " +
        "a choice is hard to undo or is a matter of taste (deleting data, a public API, a new dependency), or you are stuck after two or three attempts that failed the same way. " +
        "Don't ask what you can find out by reading files, searching or running a command. " +
        "Don't ask for permission to use tools, because the app asks when needed, and don't ask to confirm a plan you can just carry out. " +
        "Ask one short, self-contained question that says what you know and what you recommend. " +
        "Offer options when the likely answers are few, your recommendation first. " +
        "The user can always answer in their own words, skip, or leave it to you. " +
        "The result is the user's own reply: follow it like a message from them.";

    /// <inheritdoc />
    public string InputSchema => $$"""
        {
          "type": "object",
          "properties": {
            "{{QuestionArgument}}": { "type": "string", "description": "The question, with the context needed to answer it. At most {{UserQuestion.MaxQuestionLength}} characters." },
            "{{OptionsArgument}}": {
              "type": "array",
              "maxItems": {{UserQuestion.MaxOptions}},
              "description": "{{UserQuestion.MinOptions}} to {{UserQuestion.MaxOptions}} possible answers, your recommendation first. Leave out for an open question.",
              "items": {
                "type": "object",
                "properties": {
                  "{{LabelArgument}}": { "type": "string", "description": "A short answer, at most {{UserQuestion.MaxLabelLength}} characters." },
                  "{{DescriptionArgument}}": { "type": "string", "description": "Optional: what this choice means, at most {{UserQuestion.MaxDescriptionLength}} characters." }
                },
                "required": ["{{LabelArgument}}"]
              }
            },
            "{{MultiSelectArgument}}": { "type": "boolean", "description": "True when the user may pick more than one option. Defaults to false." }
          },
          "required": ["{{QuestionArgument}}"]
        }
        """;

    /// <inheritdoc />
    public bool IsReadOnly => true;

    /// <inheritdoc />
    public TimeSpan Timeout => System.Threading.Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Read the question from the arguments of an <c>ask_user</c> call.
    /// </summary>
    /// <remarks>
    /// Weak models are tolerated: options may be plain strings, or a JSON array written as a string, and <c>multi_select</c> may be the text "true" or "false".
    /// An empty options list is an open question.
    /// </remarks>
    /// <param name="arguments">The call arguments.</param>
    /// <returns>The question.</returns>
    /// <exception cref="ToolArgumentException">The arguments are not a valid question; the message says what to fix.</exception>
    public static UserQuestion Parse(ToolArguments arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var question = arguments.RequiredString(QuestionArgument).Trim();
        if (question.Length == 0)
        {
            throw new ToolArgumentException($"The '{QuestionArgument}' argument must not be empty.");
        }

        if (question.Length > UserQuestion.MaxQuestionLength)
        {
            throw new ToolArgumentException($"The question has {question.Length} characters, but at most {UserQuestion.MaxQuestionLength} are allowed. Shorten it.");
        }

        var options = ParseOptions(arguments.OptionalElement(OptionsArgument));
        var multiSelect = ParseFlag(arguments.OptionalElement(MultiSelectArgument));
        return new UserQuestion(question, options, multiSelect && options.Count > 0);
    }

    /// <summary>
    /// Read the question from the arguments of an <c>ask_user</c> call, for showing the call.
    /// </summary>
    /// <param name="argumentsJson">The call arguments as a JSON object string.</param>
    /// <returns>The question, or null when the arguments are not a valid question.</returns>
    public static UserQuestion? TryParse(string argumentsJson)
    {
        try
        {
            return Parse(ToolArguments.Parse(argumentsJson));
        }
        catch (ToolArgumentException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public string Summarize(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty(QuestionArgument, out var element)
            || element.ValueKind != JsonValueKind.String)
        {
            return string.Empty;
        }

        var line = string.Join(' ', (element.GetString() ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= SummaryLength ? line : string.Concat(line.AsSpan(0, SummaryLength), "…");
    }

    /// <inheritdoc />
    /// <remarks>
    /// Never called normally, because the agent loop asks the question itself; it only reports an error.
    /// </remarks>
    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(ToolResult.Error("This tool can only be used where the user can be asked."));
    }

    private static List<QuestionOption> ParseOptions(JsonElement? element)
    {
        if (element is not { } value)
        {
            return [];
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return [];
            }

            try
            {
                using var document = JsonDocument.Parse(text);
                return ParseOptions(document.RootElement.Clone());
            }
            catch (JsonException)
            {
                throw new ToolArgumentException($"The '{OptionsArgument}' argument must be an array of {{\"{LabelArgument}\"}} objects.");
            }
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new ToolArgumentException($"The '{OptionsArgument}' argument must be an array of {{\"{LabelArgument}\"}} objects.");
        }

        var count = value.GetArrayLength();
        if (count == 0)
        {
            return [];
        }

        if (count < UserQuestion.MinOptions || count > UserQuestion.MaxOptions)
        {
            throw new ToolArgumentException($"A question has {UserQuestion.MinOptions} to {UserQuestion.MaxOptions} options, but you gave {count}. Leave out '{OptionsArgument}' for an open question.");
        }

        List<QuestionOption> options = [];
        HashSet<string> labels = new(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < count; i++)
        {
            var option = ParseOption(value[i], i + 1);
            if (!labels.Add(option.Label))
            {
                throw new ToolArgumentException($"Option {i + 1} repeats the label '{option.Label}'. Give each option its own label.");
            }

            options.Add(option);
        }

        return options;
    }

    private static QuestionOption ParseOption(JsonElement element, int number)
    {
        string? label;
        string? description = null;
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                label = element.GetString();
                break;
            case JsonValueKind.Object:
                label = element.TryGetProperty(LabelArgument, out var labelElement) && labelElement.ValueKind == JsonValueKind.String ? labelElement.GetString() : null;
                if (element.TryGetProperty(DescriptionArgument, out var descriptionElement) && descriptionElement.ValueKind != JsonValueKind.Null)
                {
                    description = descriptionElement.ValueKind == JsonValueKind.String
                        ? descriptionElement.GetString()
                        : throw new ToolArgumentException($"Option {number} has a '{DescriptionArgument}' that is not a string.");
                }

                break;
            default:
                throw new ToolArgumentException($"Option {number} must be an object with a '{LabelArgument}', or a string.");
        }

        label = label?.Trim();
        if (string.IsNullOrEmpty(label))
        {
            throw new ToolArgumentException($"Option {number} needs a '{LabelArgument}' string.");
        }

        if (label.Length > UserQuestion.MaxLabelLength)
        {
            throw new ToolArgumentException($"Option {number} has a label of {label.Length} characters, but at most {UserQuestion.MaxLabelLength} are allowed. Shorten it and use '{DescriptionArgument}' for details.");
        }

        description = description?.Trim();
        if (description?.Length > UserQuestion.MaxDescriptionLength)
        {
            throw new ToolArgumentException($"Option {number} has a description of {description.Length} characters, but at most {UserQuestion.MaxDescriptionLength} are allowed. Shorten it.");
        }

        return new QuestionOption(label, string.IsNullOrEmpty(description) ? null : description);
    }

    private static bool ParseFlag(JsonElement? element)
    {
        return element switch
        {
            null => false,
            { ValueKind: JsonValueKind.True } => true,
            { ValueKind: JsonValueKind.False } => false,
            { ValueKind: JsonValueKind.String } text when string.Equals(text.GetString(), "true", StringComparison.OrdinalIgnoreCase) => true,
            { ValueKind: JsonValueKind.String } text when string.Equals(text.GetString(), "false", StringComparison.OrdinalIgnoreCase) => false,
            _ => throw new ToolArgumentException($"The '{MultiSelectArgument}' argument must be true or false."),
        };
    }
}
