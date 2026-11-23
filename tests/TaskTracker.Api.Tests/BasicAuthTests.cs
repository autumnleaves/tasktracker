using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace TaskTracker.Api.Tests;

/// <summary>Basic auth, and its month-long cookie, for requests through Cloudflare (with CF-Connecting-IP); none at home.</summary>
public sealed class BasicAuthTests : IDisposable
{
    private readonly TestApp _app = new(settings: new Dictionary<string, string?>
    {
        ["BasicAuth:Username"] = "tom",
        ["BasicAuth:Password"] = "correct horse",
    });

    public void Dispose()
    {
        _app.Dispose();
        _app.DeleteFolder();
    }

    private async Task<HttpResponseMessage> Get(string path, string? credentials = null, bool fromOutside = true, string? cookie = null)
    {
        using var client = _app.CreateClient(new() { HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (fromOutside)
            request.Headers.Add("CF-Connecting-IP", "203.0.113.7");
        if (credentials is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
        if (cookie is not null)
            request.Headers.Add("Cookie", $"{BasicAuth.CookieName}={cookie}");
        return await client.SendAsync(request);
    }

    private static string? LoginCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Select(v => v.Split(';')[0]).FirstOrDefault(v => v.StartsWith($"{BasicAuth.CookieName}="))?[(BasicAuth.CookieName.Length + 1)..]
            : null;

    // What the app signs, for making cookies it didn't hand out.
    private static string Signed(DateTimeOffset expires)
    {
        var expiry = expires.ToUnixTimeSeconds().ToString();
        var hmac = HMACSHA256.HashData(Encoding.UTF8.GetBytes("tom:correct horse"), Encoding.UTF8.GetBytes(expiry));
        return $"{expiry}.{Convert.ToHexStringLower(hmac)}";
    }

    [Theory]
    [InlineData("/api/board")]
    [InlineData("/")]
    [InlineData("/api/openapi.json")]
    public async Task Requests_from_outside_need_a_login(string path)
    {
        var response = await Get(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Basic", response.Headers.WwwAuthenticate.Single().Scheme);
    }

    [Theory]
    [InlineData("tom:wrong")]
    [InlineData("someone:correct horse")]
    [InlineData("tom:correct horse ")]
    public async Task Wrong_credentials_are_refused(string credentials) =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get("/api/board", credentials)).StatusCode);

    [Fact]
    public async Task The_right_credentials_get_in() =>
        Assert.Equal(HttpStatusCode.OK, (await Get("/api/board", "tom:correct horse")).StatusCode);

    [Fact]
    public async Task A_login_sets_a_cookie_for_a_month_that_lets_requests_in_without_one()
    {
        var login = await Get("/api/board", "tom:correct horse");
        var cookie = LoginCookie(login);
        var header = login.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith($"{BasicAuth.CookieName}="));

        Assert.NotNull(cookie);
        Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", header, StringComparison.OrdinalIgnoreCase);
        var expires = DateTimeOffset.FromUnixTimeSeconds(long.Parse(cookie.Split('.')[0]));
        Assert.InRange(expires - DateTimeOffset.UtcNow, TimeSpan.FromDays(29), TimeSpan.FromDays(31));
        Assert.Equal(HttpStatusCode.OK, (await Get("/api/board", cookie: cookie)).StatusCode);
    }

    [Fact]
    public async Task A_wrong_login_sets_no_cookie() =>
        Assert.Null(LoginCookie(await Get("/api/board", "tom:wrong")));

    [Fact]
    public async Task An_expired_or_altered_cookie_is_refused()
    {
        var valid = Signed(DateTimeOffset.UtcNow.AddDays(1));
        Assert.Equal(HttpStatusCode.OK, (await Get("/api/board", cookie: valid)).StatusCode);

        var later = DateTimeOffset.UtcNow.AddYears(5).ToUnixTimeSeconds();
        foreach (var cookie in new[]
        {
            Signed(DateTimeOffset.UtcNow.AddMinutes(-1)),
            $"{later}.{valid.Split('.')[1]}",
            valid[..^1] + (valid[^1] == '0' ? '1' : '0'),
            "nonsense",
        })
            Assert.Equal(HttpStatusCode.Unauthorized, (await Get("/api/board", cookie: cookie)).StatusCode);
    }

    [Fact]
    public async Task Requests_at_home_need_no_login() =>
        Assert.Equal(HttpStatusCode.OK, (await Get("/api/board", fromOutside: false)).StatusCode);

    [Fact]
    public async Task The_health_check_is_open() =>
        Assert.Equal(HttpStatusCode.OK, (await Get("/api/health")).StatusCode);

    [Fact]
    public async Task Without_a_password_there_is_no_login()
    {
        var open = new TestApp();
        try
        {
            using var client = open.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/board");
            request.Headers.Add("CF-Connecting-IP", "203.0.113.7");
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
        }
        finally
        {
            open.Dispose();
            open.DeleteFolder();
        }
    }
}
