namespace BlazeHarness.Core.Web;

/// <summary>
/// Reads the address a model gives to the fetch tool, the same way for the permission check and for the tool itself.
/// </summary>
internal static class WebUrl
{
    /// <summary>
    /// Check and read an address.
    /// </summary>
    /// <param name="text">The address text from the tool call.</param>
    /// <param name="uri">The address when it is acceptable.</param>
    /// <param name="error">What is wrong, in words the model can act on, when it is not.</param>
    /// <returns>True for an absolute http or https address with a host and no sign-in details.</returns>
    public static bool TryParse(string? text, out Uri uri, out string error)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(text) || !Uri.TryCreate(text.Trim(), UriKind.Absolute, out var parsed))
        {
            error = "The 'url' argument must be an absolute http or https address.";
            return false;
        }

        if (parsed.Scheme is not ("http" or "https") || string.IsNullOrEmpty(parsed.Host))
        {
            error = "Only http and https addresses can be fetched.";
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.UserInfo))
        {
            error = "Addresses with a user name or password in them are not fetched.";
            return false;
        }

        uri = parsed;
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Tell whether a redirect stays on the same site, where the same permission applies.
    /// </summary>
    /// <param name="from">The address that redirected.</param>
    /// <param name="to">The address it redirected to.</param>
    /// <returns>True when the host is exactly the same, ignoring the scheme and port. <c>www.example.com</c> and <c>example.com</c> are different sites, so a rule or approval for one never covers the other.</returns>
    public static bool SameSite(Uri from, Uri to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        return string.Equals(HostOf(from), HostOf(to), StringComparison.Ordinal);
    }

    /// <summary>
    /// Get the host the way permission rules and approvals see it.
    /// </summary>
    /// <param name="uri">The address.</param>
    /// <returns>The lower-case host, without a trailing dot, so <c>Example.com.</c> and <c>example.com</c> are the same host.</returns>
    public static string HostOf(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.IdnHost.TrimEnd('.');
    }
}
