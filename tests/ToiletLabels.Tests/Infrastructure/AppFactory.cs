using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ToiletLabels.Labels;
using ToiletLabels.Users;

namespace ToiletLabels.Tests;

/// <summary>
/// Spustí aplikaci proti Azurite s unikátními jmény tabulek a kontejnerů,
/// takže testy se navzájem neovlivňují.
/// </summary>
public sealed class AppFactory : WebApplicationFactory<Program>
{
    public const string TestPassword = "test-password";

    private readonly Dictionary<string, string> _settings;
    private readonly List<Action<IServiceCollection>> _serviceOverrides = [];

    public string LabelsTable { get; }
    public string UsersTable { get; }
    public string ImagesContainer { get; }
    public string KeysContainer { get; }

    public AppFactory(AzuriteFixture azurite) : this(azurite, null)
    {
    }

    /// <summary>Druhá instance aplikace nad stejnými tabulkami a kontejnery (např. simulace restartu).</summary>
    public AppFactory(AzuriteFixture azurite, AppFactory? sharedStorageWith)
    {
        LabelsTable = sharedStorageWith?.LabelsTable ?? "Labels" + Hex();
        UsersTable = sharedStorageWith?.UsersTable ?? "Users" + Hex();
        ImagesContainer = sharedStorageWith?.ImagesContainer ?? "img-" + Hex();
        KeysContainer = sharedStorageWith?.KeysContainer ?? "keys-" + Hex();

        _settings = new Dictionary<string, string>
        {
            ["ConnectionStrings:Storage"] = azurite.ConnectionString,
            ["Storage:LabelsTable"] = LabelsTable,
            ["Storage:UsersTable"] = UsersTable,
            ["Storage:ImagesContainer"] = ImagesContainer,
            ["Storage:KeysContainer"] = KeysContainer,
            ["Storage:PublicBlobBaseUrl"] = "http://blob.test/",
            ["Mapy:ApiKey"] = "test-key",
        };
    }

    /// <summary>Vymění služby v kontejneru (např. IGeocoder za fake). Volat před prvním CreateClient.</summary>
    public AppFactory ConfigureTestServices(Action<IServiceCollection> configure)
    {
        _serviceOverrides.Add(configure);
        return this;
    }

    /// <summary>Doplní nebo přepíše konfigurační hodnotu. Volat před prvním CreateClient.</summary>
    public AppFactory WithSetting(string key, string value)
    {
        _settings[key] = value;
        return this;
    }

    /// <summary>Založí uživatele přímo v tabulce (bez přihlášení).</summary>
    public async Task CreateUserAsync(string userName, string password, bool isAdmin)
    {
        var hasher = Services.GetRequiredService<IPasswordHasher<AppUser>>();
        var placeholder = new AppUser(userName, "", isAdmin, CreatedTimestamp.Now(TimeProvider.System));
        var created = await Services.GetRequiredService<IUserRepository>().TryCreateAsync(
            placeholder with { PasswordHash = hasher.HashPassword(placeholder, password) },
            TestContext.Current.CancellationToken);
        Assert.True(created, $"User '{userName}' already exists.");
    }

    /// <summary>Přihlásí klienta přes formulář /login/. Vrátí odpověď (302, nebo 200 při neúspěchu).</summary>
    public async Task<HttpResponseMessage> LoginAsync(HttpClient client, string userName, string password, string? next = null)
    {
        var ct = TestContext.Current.CancellationToken;
        var url = next is null ? "/login/" : "/login/?next=" + Uri.EscapeDataString(next);
        var token = await client.GetAntiforgeryTokenAsync(url, ct);
        return await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = userName,
            ["password"] = password,
            ["__RequestVerificationToken"] = token,
        }), ct);
    }

    /// <summary>Klient přihlášený jako nový administrátor. Nesleduje přesměrování, drží cookies.</summary>
    public Task<HttpClient> CreateAdminClientAsync() => CreateUserClientAsync(isAdmin: true);

    /// <summary>Klient přihlášený jako nový uživatel. Nesleduje přesměrování, drží cookies.</summary>
    public async Task<HttpClient> CreateUserClientAsync(bool isAdmin)
    {
        var userName = (isAdmin ? "admin-" : "user-") + Hex();
        await CreateUserAsync(userName, TestPassword, isAdmin);

        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await LoginAsync(client, userName, TestPassword);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // UseSetting je vidět už při buildu hostu, tedy i pro hodnoty čtené přímo v Program.cs.
        foreach (var (key, value) in _settings)
            builder.UseSetting(key, value);

        builder.ConfigureTestServices(services =>
        {
            services.AddTransient<IStartupFilter, WhoAmIStartupFilter>();
            foreach (var configure in _serviceOverrides)
                configure(services);
        });
    }

    private static string Hex() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Testovací endpoint <c>/__whoami</c>: 200 se jménem přihlášeného uživatele, jinak 401.
    /// Ověřuje cookie standardním schématem, takže odhalí i cookie, kterou aplikace nedokáže rozšifrovat.
    /// </summary>
    private sealed class WhoAmIStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, pipeline) =>
            {
                if (context.Request.Path != "/__whoami")
                {
                    await pipeline();
                    return;
                }

                var result = await context.AuthenticateAsync();
                if (result.Succeeded)
                {
                    context.Response.StatusCode = StatusCodes.Status200OK;
                    await context.Response.WriteAsync(result.Principal.FindFirstValue(ClaimTypes.Name) ?? "");
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                }
            });
            next(app);
        };
    }
}
