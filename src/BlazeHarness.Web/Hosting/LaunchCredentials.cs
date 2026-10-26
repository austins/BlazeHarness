using System.Security.Cryptography;
using System.Text;

namespace BlazeHarness.Web.Hosting;

/// <summary>
/// Per-launch secrets. The token appears once in the launch URL and is traded for a session secret that
/// lives only in an <c>HttpOnly</c> cookie, so a leaked URL stops working as soon as the browser used it.
/// </summary>
public sealed class LaunchCredentials
{
    public const string CookieName = "bh_session";

    private int _tokenConsumed;

    public LaunchCredentials()
    {
        Token = NewSecret();
        SessionSecret = NewSecret();
    }

    public string Token { get; }

    public string SessionSecret { get; }

    public bool TryConsumeToken(string? candidate)
    {
        if (!FixedTimeEquals(candidate, Token))
        {
            return false;
        }

        return Interlocked.Exchange(ref _tokenConsumed, 1) == 0;
    }

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
