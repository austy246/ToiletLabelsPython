using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ToiletLabels.Geo;
using ToiletLabels.Labels;
using ToiletLabels.Users;

namespace ToiletLabels.Tests;

/// <summary>
/// Spustí aplikaci proti Azurite s unikátními jmény tabulek a kontejnerů,
/// takže testy se navzájem neovlivňují.
/// </summary>
public sealed partial class AppFactory : WebApplicationFactory<Program>
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
        // Dřív než se cokoli spustí: testy nesmí nikdy sáhnout na jiné storage než lokální Azurite.
        AssertLocalStorage(_settings.GetValueOrDefault("ConnectionStrings:Storage"));
        AssertLocalStorage(Environment.GetEnvironmentVariable("ConnectionStrings__Storage"));

        builder.UseEnvironment("Development");

        // UseSetting je vidět už při buildu hostu, tedy i pro hodnoty čtené přímo v Program.cs.
        foreach (var (key, value) in _settings)
            builder.UseSetting(key, value);

        builder.ConfigureTestServices(services =>
        {
            services.AddTransient<IStartupFilter, WhoAmIStartupFilter>();
            services.AddTransient<IStartupFilter, ThrowStartupFilter>();
            // Výchozí geokodér bez sítě (testy ho mohou přepsat); nikdy nesmí jít dotaz na Nominatim.
            services.AddSingleton<IGeocoder>(new FakeGeocoder());
            foreach (var configure in _serviceOverrides)
                configure(services);
        });
    }

    private static string Hex() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Selže, pokud connection string nemíří výhradně na 127.0.0.1/localhost (Azurite). Prázdná hodnota projde
    /// (proměnná prostředí nemusí být nastavená); chybějící endpoint v neprázdném řetězci ne.
    /// </summary>
    internal static void AssertLocalStorage(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var hosts = EndpointPattern().Matches(connectionString).Select(m => m.Groups[1].Value).ToList();
        if (hosts.Count == 0 || hosts.Any(h => h is not ("127.0.0.1" or "localhost")))
        {
            throw new InvalidOperationException(
                "Tests must run against local Azurite only: ConnectionStrings:Storage has a non-local or missing endpoint.");
        }
    }

    [GeneratedRegex(@"https?://([^:/;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex EndpointPattern();

    /// <summary>Testovací endpoint <c>/__throw</c>: vyhodí výjimku z pipeline za exception handlerem aplikace.</summary>
    private sealed class ThrowStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            // Za aplikačním middleware a endpointy: /__throw žádný endpoint nemá, takže sem požadavek propadne.
            app.Use((context, pipeline) =>
                context.Request.Path == "/__throw"
                    ? throw new InvalidOperationException("secret-internal-detail")
                    : pipeline());
        };
    }

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
