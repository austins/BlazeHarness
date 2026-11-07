using BlazeHarness.Web.Resources;
using Microsoft.AspNetCore.Localization;

namespace BlazeHarness.Web.Hosting;

/// <summary>
/// The endpoint that stores the display language in the culture cookie.
/// </summary>
/// <remarks>
/// The server sets the cookie because Chromium stores a cookie written to <c>document.cookie</c> asynchronously. A reload
/// right after the write can go out without it, so the page renders in the old language while the circuit, which
/// connects later, uses the new one. A <c>Set-Cookie</c> header is stored before the browser hands over the response.
/// </remarks>
internal static class CultureEndpoints
{
    /// <summary>
    /// The route that sets the display language.
    /// </summary>
    internal const string Route = "/api/culture/{culture}";

    extension(WebApplication app)
    {
        /// <summary>
        /// Add the endpoint that sets the display language.
        /// </summary>
        /// <returns>The same app, to chain calls.</returns>
        internal WebApplication MapCulture()
        {
            ArgumentNullException.ThrowIfNull(app);
            app.MapPost(Route, Set);
            return app;
        }
    }

    /// <summary>
    /// Store a display language in the culture cookie, for both formatting and UI text.
    /// </summary>
    /// <param name="culture">The culture name of one of the UI languages.</param>
    /// <param name="languages">The UI languages the app has.</param>
    /// <param name="context">The current request.</param>
    /// <returns>No content, or a bad request for a culture that is not a UI language.</returns>
    internal static IResult Set(string culture, UiLanguages languages, HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(languages);
        ArgumentNullException.ThrowIfNull(context);

        if (!languages.All.Any(language => language.Culture == culture))
        {
            return Results.BadRequest();
        }

#pragma warning disable S2092 // The app serves plain HTTP, where a browser drops a Secure cookie.
        context.Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions
            {
                Path = "/",
                MaxAge = TimeSpan.FromDays(365),
                SameSite = SameSiteMode.Lax,
                HttpOnly = true,
                IsEssential = true,
            });
#pragma warning restore S2092
        return Results.NoContent();
    }
}
