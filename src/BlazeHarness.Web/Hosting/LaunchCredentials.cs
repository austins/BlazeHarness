using System.Security.Cryptography;
using System.Text;

namespace BlazeHarness.Web.Hosting;

/// <summary>
/// Per-launch secrets. The token appears once in the launch URL and is traded for a session secret that
/// lives only in an <c>HttpOnly</c> cookie, so a leaked URL stops working as soon as the browser used it.
/// </summary>
public sealed class LaunchCredentials
{
    /// <summary>
    /// The name of the session cookie.
    /// </summary>
    public const string CookieName = "bh_session";

    private int _tokenConsumed;

    /// <summary>
    /// Initializes a new instance of the <see cref="LaunchCredentials"/> class with fresh random secrets.
    /// </summary>
    public LaunchCredentials()
    {
        Token = NewSecret();
        SessionSecret = NewSecret();
    }

    /// <summary>
    /// Gets the single-use token embedded in the launch URL.
    /// </summary>
    public string Token { get; }

    /// <summary>
    /// Gets the value of the session cookie that is issued when the token is exchanged.
    /// </summary>
    public string SessionSecret { get; }

    /// <summary>
    /// Try to use the launch token.
    /// </summary>
    /// <remarks>
    /// The comparison takes constant time, and only the first successful call returns true.
    /// </remarks>
    /// <param name="candidate">The token presented by the browser.</param>
    /// <returns>True when <paramref name="candidate"/> is the token and it had not been used before.</returns>
    public bool TryConsumeToken(string? candidate)
    {
        if (!FixedTimeEquals(candidate, Token))
        {
            return false;
        }

        return Interlocked.Exchange(ref _tokenConsumed, 1) == 0;
    }

    /// <summary>
    /// Check a session cookie.
    /// </summary>
    /// <param name="cookieValue">The cookie value sent by the browser.</param>
    /// <returns>True when it equals <see cref="SessionSecret"/>; compared in constant time.</returns>
    public bool IsValidSession(string? cookieValue)
    {
        return FixedTimeEquals(cookieValue, SessionSecret);
    }

    private static string NewSecret()
    {
        return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    }

    private static bool FixedTimeEquals(string? candidate, string expected)
    {
        if (candidate is null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(candidate), Encoding.UTF8.GetBytes(expected));
    }
}
