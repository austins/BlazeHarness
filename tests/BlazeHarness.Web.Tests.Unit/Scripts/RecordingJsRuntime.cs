using Microsoft.JSInterop;

namespace BlazeHarness.Web.Tests.Unit.Scripts;

#pragma warning disable BL0016 // This fake implements IJSRuntime, so its calls to its own methods are not browser interop that can disconnect.

/// <summary>
/// An <see cref="IJSRuntime"/> that records each call and returns a configured result or throws a configured exception.
/// </summary>
internal sealed class RecordingJsRuntime : IJSRuntime
{
    /// <summary>
    /// Gets the calls made so far, in order.
    /// </summary>
    public List<(string Identifier, object?[] Args)> Calls { get; } = [];

    /// <summary>
    /// Gets or sets the value a call returns.
    /// </summary>
    public object? Result { get; set; }

    /// <summary>
    /// Gets or sets the exception a call throws.
    /// </summary>
    public Exception? Failure { get; set; }

    /// <inheritdoc />
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => Record<TValue>(identifier, args);

    /// <inheritdoc />
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
        Record<TValue>(identifier, args);

    private ValueTask<TValue> Record<TValue>(string identifier, object?[]? args)
    {
        Calls.Add((identifier, args ?? []));
        if (Failure is not null)
        {
            return ValueTask.FromException<TValue>(Failure);
        }

        return ValueTask.FromResult(Result is TValue value ? value : default!);
    }
}

#pragma warning restore BL0016
