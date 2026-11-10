namespace BlazeHarness.Core.Diff;

/// <summary>
/// A run of characters in a text.
/// </summary>
/// <param name="Start">The index of the first character.</param>
/// <param name="Length">How many characters the run has.</param>
public readonly record struct TextRange(int Start, int Length);
