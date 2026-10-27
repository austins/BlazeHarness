namespace BlazeHarness.Core.Permissions;

/// <summary>
/// How a command spells and matches its options.
/// </summary>
internal enum FlagStyle
{
    /// <summary>
    /// Unix options: <c>-r</c> (which may be combined, as in <c>-rf</c>) and <c>--recursive</c>, matched by case.
    /// </summary>
    Posix,

    /// <summary>
    /// <c>cmd.exe</c> switches such as <c>/s</c>, which ignore case and can be joined, as in <c>/s/q</c>.
    /// </summary>
    Windows,

    /// <summary>
    /// PowerShell parameters such as <c>-Recurse</c>, which ignore case and may be shortened to a prefix.
    /// </summary>
    PowerShell,
}
