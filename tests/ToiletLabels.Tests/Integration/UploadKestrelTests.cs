using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using ToiletLabels.Geo;
using ToiletLabels.Labels;
using ToiletLabels.Tests.Infrastructure;

namespace ToiletLabels.Tests.Integration;

/// <summary>
/// Limit těla požadavku na skutečném Kestrelu (TestServer limity Kestrelu nevynucuje). Případ se známým
/// Content-Length nad limitem se tu netestuje: Kestrel odpoví 413 a zavře spojení, takže klient, který
/// ještě odesílá tělo, dostane místo odpovědi přerušené spojení.
/// </summary>
public class UploadKestrelTests(AzuriteFixture azurite)
{
    private const long Limit = 52_428_800;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AppFactory CreateFactory(AzuriteFixture azurite, out Uri address)
    {
        var factory = new AppFactory(azurite).ConfigureTestServices(s => s.AddSingleton<IGeocoder>(new FakeGeocoder()));
        // Pevný volný port, aby šlo klientovi nastavit adresu.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        factory.UseKestrel(port);
        address = new Uri($"http://localhost:{port}");
        return factory;
    }

    /// <summary>Klient přímo na Kestrel (klienti továrny tu nesledují přesměrování podle nastavení).</summary>
    private static async Task<HttpClient> LoginAsync(AppFactory factory, Uri address)
    {
        var userName = "admin-" + Guid.NewGuid().ToString("N")[..8];
        await factory.CreateUserAsync(userName, AppFactory.TestPassword, isAdmin: true);
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = address };
        var login = await factory.LoginAsync(client, userName, AppFactory.TestPassword);
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        return client;
    }

    [Fact]
    public async Task Upload_OverLimitWithoutContentLength_Returns413_OnKestrel()
    {
        await using var factory = CreateFactory(azurite, out var address);
        using var client = await LoginAsync(factory, address);
        var token = await client.GetAntiforgeryTokenAsync("/upload/", Ct);
        using var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new UnknownLengthContent(new byte[Limit + 1]), "men_image", "m.jpg" },
        };

        var response = await client.PostAsync("/upload/", form, Ct);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(await factory.Services.GetRequiredService<ILabelRepository>().ListAsync(Ct));
    }

    [Fact]
    public async Task Upload_Under50Mb_Works_OnKestrel()
    {
        await using var factory = CreateFactory(azurite, out var address);
        using var client = await LoginAsync(factory, address);

        // Větší než výchozí limit Kestrelu (~28,6 MB), ale pod 50 MB; platný JPEG s dlouhým koncem.
        var big = TestImages.Jpeg(100, 100).Concat(new byte[40_000_000]).ToArray();
        var response = await client.PostFormAsync("/upload/", null,
            new FormFile("men_image", "m.jpg", big),
            new FormFile("women_image", "w.jpg", TestImages.Jpeg(50, 50)));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Single(await factory.Services.GetRequiredService<ILabelRepository>().ListAsync(Ct));
    }
}
