namespace BlazeHarness.Core.Providers;

/// <summary>
/// Which problem a <see cref="ValidationIssue"/> reports, so a UI can show it in the user's language.
/// </summary>
public enum ValidationCode
{
    /// <summary>The provider has no identifier.</summary>
    IdRequired,

    /// <summary>The provider has no name.</summary>
    NameRequired,

    /// <summary>The base URL is not an absolute http or https address.</summary>
    BaseUrlInvalid,

    /// <summary>The API key reference is not an environment variable reference.</summary>
    ApiKeyRefInvalid,

    /// <summary>The thinking level is sent in a body field, but no field is named.</summary>
    ThinkingFieldRequired,

    /// <summary>The body field for the thinking level is not a valid path.</summary>
    ThinkingFieldInvalid,

    /// <summary>A model has no id.</summary>
    ModelIdRequired,

    /// <summary>A model id appears more than once; the <c>id</c> argument holds it.</summary>
    ModelIdDuplicate,

    /// <summary>The context window is not a positive number.</summary>
    ContextWindowInvalid,

    /// <summary>Max output tokens is not a positive number.</summary>
    MaxOutputInvalid,

    /// <summary>Max output tokens is not below the context window.</summary>
    MaxOutputTooLarge,

    /// <summary>The default thinking level is not one the model supports.</summary>
    DefaultLevelInvalid,

    /// <summary>An extra field name is not a valid path; the <c>name</c> argument holds it.</summary>
    ExtraFieldInvalid,

    /// <summary>A sampling value is outside its range; the <c>key</c>, <c>min</c> and <c>max</c> arguments describe it.</summary>
    SamplingOutOfRange,
}
