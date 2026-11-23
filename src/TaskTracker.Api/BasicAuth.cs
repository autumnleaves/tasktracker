using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TaskTracker.Api;

/// <summary>
/// HTTP basic auth for requests that come in through Cloudflare (the tunnel), so
/// https://tasktracker.autumnleaves.net needs a login while http://cargoship:5280 at home and on
/// the tailnet doesn't. Cloudflare adds <c>CF-Connecting-IP</c> to every request it proxies and a
/// client can't remove it, so its presence marks a request from outside. Off unless
/// <c>BasicAuth:Password</c> is set; <c>/api/health</c> is always open, for deploy checks.
/// <para>
/// A login also sets a cookie good for <see cref="CookieLifetime"/>, so a browser only asks again
/// once it runs out. The cookie is its expiry time signed with the credentials: changing the
/// password signs everyone out.
/// </para>
/// </summary>
public static class BasicAuth
{
    public const string CookieName = "tasktracker_login";
    public static readonly TimeSpan CookieLifetime = TimeSpan.FromDays(30);

    public static IApplicationBuilder UseBasicAuthFromOutside(this WebApplication app)
    {
        var section = app.Configuration.GetSection("BasicAuth");
        var user = section["Username"] is { Length: > 0 } u ? u : "tom";
        var password = section["Password"];
        if (string.IsNullOrEmpty(password))
        {
            app.Logger.LogWarning("BasicAuth:Password isn't set: requests through Cloudflare need no login");
            return app;
        }

        var credentials = Encoding.UTF8.GetBytes($"{user}:{password}");
        var expected = SHA256.HashData(credentials);
        var time = app.Services.GetRequiredService<TimeProvider>();

        return app.Use(async (context, next) =>
        {
            var request = context.Request;
            if (!request.Headers.ContainsKey("CF-Connecting-IP")
                || request.Path.Equals("/api/health", StringComparison.OrdinalIgnoreCase)
                || CookieIsValid(request.Cookies[CookieName], credentials, time.GetUtcNow()))
            {
                await next(context);
                return;
            }

            if (Matches(request.Headers.Authorization.ToString(), expected))
            {
                var expires = time.GetUtcNow() + CookieLifetime;
                context.Response.Cookies.Append(CookieName, Cookie(expires, credentials), new CookieOptions
                {
                    Expires = expires,
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Lax,
                    IsEssential = true,
                });
                await next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Basic realm=\"tasktracker\", charset=\"UTF-8\"";
        });
    }

    private static bool Matches(string header, byte[] expected)
    {
        const string scheme = "Basic ";
        if (!header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            return false;
        string credentials;
        try { credentials = Encoding.UTF8.GetString(Convert.FromBase64String(header[scheme.Length..].Trim())); }
        catch (FormatException) { return false; }
        // Compare hashes, so the time taken doesn't depend on how much of the password matched.
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(credentials)), expected);
    }

    // "<expiry, Unix seconds>.<HMAC of that, keyed with the credentials>"
    private static string Cookie(DateTimeOffset expires, byte[] credentials)
    {
        var expiry = expires.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        return $"{expiry}.{Sign(expiry, credentials)}";
    }

    private static bool CookieIsValid(string? cookie, byte[] credentials, DateTimeOffset now)
    {
        if (cookie?.Split('.') is not [var expiry, var signature]
            || !long.TryParse(expiry, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || seconds <= now.ToUnixTimeSeconds())
            return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(signature), Encoding.ASCII.GetBytes(Sign(expiry, credentials)));
    }

    private static string Sign(string expiry, byte[] credentials) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(credentials, Encoding.UTF8.GetBytes(expiry)));
}
