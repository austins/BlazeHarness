namespace BlazeHarness.Tests.Shared;

/// <summary>A <see cref="LoggerFake"/> for the category of one class.</summary>
/// <typeparam name="TCategoryName">The class the logger belongs to.</typeparam>
public sealed class LoggerFake<TCategoryName>() : LoggerFake(typeof(TCategoryName)), ILogger<TCategoryName>;
