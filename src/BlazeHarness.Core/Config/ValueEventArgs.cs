namespace BlazeHarness.Core.Config;

/// <summary>
/// Event data carrying a value.
/// </summary>
/// <typeparam name="T">The value type.</typeparam>
/// <param name="value">The value.</param>
public sealed class ValueEventArgs<T>(T value) : EventArgs
{
    /// <summary>
    /// Gets the value.
    /// </summary>
    public T Value { get; } = value;
}
