namespace BlazeHarness.Core.Git;

/// <summary>
/// Runs the git program.
/// </summary>
internal interface IGitRunner
{
    /// <summary>
    /// Run git with some arguments.
    /// </summary>
    /// <param name="workingDirectory">The folder to run in.</param>
    /// <param name="arguments">The arguments after <c>git</c>.</param>
    /// <param name="cancellationToken">A token that stops the run.</param>
    /// <returns>The result; <see cref="GitResult.Unavailable"/> when git is missing or took too long.</returns>
    Task<GitResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}
