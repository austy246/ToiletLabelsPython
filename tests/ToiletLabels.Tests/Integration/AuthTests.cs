using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ToiletLabels.Tests.Integration;

public class AuthTests(AzuriteFixture azurite)
{
    private const string AuthCookie = "tl_auth";

    private static WebApplicationFactoryClientOptions NoRedirect => new() { AllowAutoRedirect = false };

    [Fact]
    public async Task Login_Valid_SetsCookieAndRedirectsToNext()
    {
        await using var factory = new AppFactory(azurite);
        await factory.CreateUserAsync("austy", "pw-1", isAdmin: true);
        var client = factory.CreateClient(NoRedirect);

        var res = await factory.LoginAsync(client, "austy", "pw-1", next: "/upload/");

        Assert.Equal(HttpStatusCode.Found, res.StatusCode);
        Assert.Equal("/upload/", res.Headers.Location?.OriginalString);
        Assert.NotNull(AuthCookieOf(res));
        var whoAmI = await client.GetAsync("/__whoami", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, whoAmI.StatusCode);
        Assert.Equal("austy", await whoAmI.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("//evil.example/path")]
    public async Task Login_ExternalNext_RedirectsHome(string next)
    {
        await using var factory = new AppFactory(azurite);
        await factory.CreateUserAsync("austy", "pw-1", isAdmin: true);
        var client = factory.CreateClient(NoRedirect);

        var res = await factory.LoginAsync(client, "austy", "pw-1", next);

        Assert.Equal(HttpStatusCode.Found, res.StatusCode);
        Assert.Equal("/", res.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Login_WithoutNext_RedirectsHome()
    {
        await using var factory = new AppFactory(azurite);
        await factory.CreateUserAsync("austy", "pw-1", isAdmin: true);
        var client = factory.CreateClient(NoRedirect);

        var res = await factory.LoginAsync(client, "austy", "pw-1");

        Assert.Equal(HttpStatusCode.Found, res.StatusCode);
        Assert.Equal("/", res.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("austy", "wrong")]
    [InlineData("nobody", "pw-1")]
    public async Task Login_WrongCredentials_ShowsError(string userName, string password)
    {
        await using var factory = new AppFactory(azurite);
        await factory.CreateUserAsync("austy", "pw-1", isAdmin: true);
        var client = factory.CreateClient(NoRedirect);

        var res = await factory.LoginAsync(client, userName, password);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("Please enter a correct username and password.",
            await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Null(AuthCookieOf(res));
    }

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Production", true)]
    public async Task Login_Cookie_HasExpectedFlags(string environment, bool secure)
    {
        await using var baseFactory = new AppFactory(azurite);
        await baseFactory.CreateUserAsync("austy", "pw-1", isAdmin: true);
        await using var factory = baseFactory.WithWebHostBuilder(b => b.UseEnvironment(environment));
        var client = factory.CreateClient(NoRedirect);

        // Žádost jde po http: Development cookie Secure nenastaví, Production ji vynutí vždy.
        var res = await baseFactory.LoginAsync(client, "austy", "pw-1");

        Assert.Equal(HttpStatusCode.Found, res.StatusCode);
        var setCookie = res.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith(AuthCookie + "=", StringComparison.Ordinal));
        Assert.Equal(secure, setCookie.Contains("secure", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_UserNameIsCaseInsensitive()
    {
        await using var factory = new AppFactory(azurite);
        await factory.CreateUserAsync("austy", "pw-1", isAdmin: true);
        var client = factory.CreateClient(NoRedirect);

        var res = await factory.LoginAsync(client, "AUSTY", "pw-1");

        Assert.Equal(HttpStatusCode.Found, res.StatusCode);
        var whoAmI = await client.GetStringAsync("/__whoami", TestContext.Current.CancellationToken);
        Assert.Equal("austy", whoAmI);
    }

    [Fact]
    public async Task Login_PageHasNoForgotPasswordLink()
    {
        await using var factory = new AppFactory(azurite);
        var client = factory.CreateClient();

        var html = await client.GetStringAsync("/login/", TestContext.Current.CancellationToken);

        Assert.DoesNotContain("Forgot password", html);
        Assert.Contains("Sign in to Toilet Labels", html);
    }

    [Fact]
    public async Task Login_PostWithoutAntiforgeryToken_IsRejected()
    {
        await using var factory = new AppFactory(azurite);
        await factory.CreateUserAsync("austy", "pw-1", isAdmin: true);
        var client = factory.CreateClient(NoRedirect);

        var res = await client.PostAsync("/login/", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "austy",
            ["password"] = "pw-1",
        }), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Startup_SeedsAdminFromConfig()
    {
        await using var factory = new AppFactory(azurite)
            .WithSetting("Admin:UserName", "seeded")
            .WithSetting("Admin:Password", "pw-seed");
        var client = factory.CreateClient(NoRedirect);

        var res = await factory.LoginAsync(client, "seeded", "pw-seed");

        Assert.Equal(HttpStatusCode.Found, res.StatusCode);
        Assert.Equal("seeded", await client.GetStringAsync("/__whoami", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Logout_Post_SignsOut()
    {
        await using var factory = new AppFactory(azurite);
        var client = await factory.CreateAdminClientAsync();
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/__whoami", ct)).StatusCode);

        var token = await client.GetAntiforgeryTokenAsync("/login/", ct);
        var res = await client.PostAsync("/logout/", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        }), ct);

        Assert.Equal(HttpStatusCode.Found, res.StatusCode);
        Assert.Equal("/", res.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/__whoami", ct)).StatusCode);
    }

    [Fact]
    public async Task Logout_PostWithoutAntiforgeryToken_IsRejectedAndKeepsSession()
    {
        await using var factory = new AppFactory(azurite);
        var client = await factory.CreateAdminClientAsync();
        var ct = TestContext.Current.CancellationToken;

        var res = await client.PostAsync("/logout/", new FormUrlEncodedContent(new Dictionary<string, string>()), ct);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/__whoami", ct)).StatusCode);
    }

    [Fact]
    public async Task Logout_Get_NotAllowed()
    {
        await using var factory = new AppFactory(azurite);
        var client = await factory.CreateAdminClientAsync();
        var ct = TestContext.Current.CancellationToken;

        var res = await client.GetAsync("/logout/", ct);

        Assert.True(res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, $"Got {res.StatusCode}.");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/__whoami", ct)).StatusCode);
    }

    [Fact]
    public async Task AuthCookie_SurvivesNewAppInstance()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var first = new AppFactory(azurite);
        await first.CreateUserAsync("austy", "pw-1", isAdmin: true);
        var login = await first.LoginAsync(first.CreateClient(NoRedirect), "austy", "pw-1");
        var cookie = AuthCookieOf(login);
        Assert.NotNull(cookie);

        // Nová instance nad stejným úložištěm (včetně kontejneru s klíči) cookie přijme.
        await using var second = new AppFactory(azurite, sharedStorageWith: first);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/__whoami");
        request.Headers.Add("Cookie", $"{AuthCookie}={cookie}");
        var res = await second.CreateClient().SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("austy", await res.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public async Task AuthCookie_IsRejectedByInstanceWithDifferentKeys()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var first = new AppFactory(azurite);
        await first.CreateUserAsync("austy", "pw-1", isAdmin: true);
        var cookie = AuthCookieOf(await first.LoginAsync(first.CreateClient(NoRedirect), "austy", "pw-1"));
        Assert.NotNull(cookie);

        await using var unrelated = new AppFactory(azurite);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/__whoami");
        request.Headers.Add("Cookie", $"{AuthCookie}={cookie}");
        var res = await unrelated.CreateClient().SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    /// <summary>Hodnota cookie <c>tl_auth</c> ze Set-Cookie odpovědi, nebo <c>null</c>.</summary>
    private static string? AuthCookieOf(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
            return null;
        var prefix = AuthCookie + "=";
        return values
            .Where(v => v.StartsWith(prefix, StringComparison.Ordinal))
            .Select(v => v[prefix.Length..].Split(';')[0])
            .FirstOrDefault(v => v.Length > 0);
    }
}
