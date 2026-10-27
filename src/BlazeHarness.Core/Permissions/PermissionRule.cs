namespace BlazeHarness.Core.Permissions;

/// <summary>
/// One rule the user wrote to allow, ask about or refuse a kind of tool call.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="PermissionVerdict.Deny"/> rule always wins, at every access level, and so do the protected folders.
/// Otherwise the last matching rule decides, so a later rule can narrow an earlier one.
/// An <see cref="PermissionVerdict.Allow"/> rule only skips the question: it never lets a call out of the sandbox or past a protected folder.
/// An <see cref="PermissionVerdict.Ask"/> rule asks every time, under every approval mode, and the answer can't be remembered.
/// </para>
/// <para>
/// For a shell command the pattern is matched against the whole command and against each simple command in it, written as the program and its arguments,
/// where <c>*</c> stands for any text and <c>?</c> for one character. An allow rule only applies when every simple command in the call matches it.
/// For a path the pattern is matched against the path relative to the working folder, and against the full path, where <c>*</c> stays inside one folder,
/// <c>**</c> crosses folders and <c>?</c> is one character. Use <c>/</c> as the separator on every system.
/// For an MCP tool the pattern is matched against <c>server/tool</c>, where <c>*</c> stands for any text and <c>?</c> for one character.
/// </para>
/// </remarks>
/// <param name="Target">What the pattern is matched against.</param>
/// <param name="Action">What to do when the pattern matches.</param>
/// <param name="Pattern">The pattern.</param>
public sealed record PermissionRule(RuleTarget Target, PermissionVerdict Action, string Pattern);
