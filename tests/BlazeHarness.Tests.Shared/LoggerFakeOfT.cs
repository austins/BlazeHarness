namespace BlazeHarness.Tests.Shared;

/// <summary>A <see cref="LoggerFake"/> for the category of one class.</summary>
/// <typeparam name="TCategoryName">The class the logger belongs to.</typeparam>
#pragma warning disable SA1649 // LoggerFake.cs holds the non-generic base class, so this generic sibling uses LoggerFakeOfT.cs.
public sealed class LoggerFake<TCategoryName>() : LoggerFake(typeof(TCategoryName)), ILogger<TCategoryName>;
#pragma warning restore SA1649
