using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ToiletLabels.Tests;

/// <summary>
/// Spustí aplikaci proti Azurite s unikátními jmény tabulek a kontejnerů,
/// takže testy se navzájem neovlivňují.
/// </summary>
public sealed class AppFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string> _settings;
    private readonly List<Action<IServiceCollection>> _serviceOverrides = [];

    public string LabelsTable { get; }
    public string UsersTable { get; }
    public string ImagesContainer { get; }
    public string KeysContainer { get; }

    public AppFactory(AzuriteFixture azurite)
    {
        LabelsTable = "Labels" + Hex();
        UsersTable = "Users" + Hex();
        ImagesContainer = "img-" + Hex();
        KeysContainer = "keys-" + Hex();

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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // UseSetting je vidět už při buildu hostu, tedy i pro hodnoty čtené přímo v Program.cs.
        foreach (var (key, value) in _settings)
            builder.UseSetting(key, value);

        builder.ConfigureTestServices(services =>
        {
            foreach (var configure in _serviceOverrides)
                configure(services);
        });
    }

    private static string Hex() => Guid.NewGuid().ToString("N")[..8];
}
