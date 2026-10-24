using System.Net;
using System.Text.RegularExpressions;
using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using ToiletLabels.Geo;
using ToiletLabels.Labels;
using ToiletLabels.Tests.Infrastructure;

namespace ToiletLabels.Tests.Integration;

public class EditTests(AzuriteFixture azurite)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (AppFactory Factory, FakeGeocoder Geocoder) CreateFactory(AzuriteFixture azurite)
    {
        var geocoder = new FakeGeocoder();
        var factory = new AppFactory(azurite).ConfigureTestServices(s => s.AddSingleton<IGeocoder>(geocoder));
        return (factory, geocoder);
    }

    private static Dictionary<string, string> Fields(params (string Name, string Value)[] extra)
    {
        var fields = new Dictionary<string, string>
        {
            ["place"] = "New place",
            ["description"] = "New description",
            ["country"] = "Německo",
            ["city"] = "Berlin",
        };
        foreach (var (name, value) in extra)
            fields[name] = value;
        return fields;
    }

    private static BlobContainerClient Container(AppFactory factory) =>
        factory.Services.GetRequiredService<BlobServiceClient>().GetBlobContainerClient(factory.ImagesContainer);

    private static async Task<List<string>> BlobNamesAsync(AppFactory factory)
    {
        var names = new List<string>();
        await foreach (var blob in Container(factory).GetBlobsAsync(cancellationToken: Ct))
            names.Add(blob.Name);
        names.Sort(StringComparer.Ordinal);
        return names;
    }

    private static async Task<Label> GetLabelAsync(AppFactory factory, string id) =>
        (await factory.Services.GetRequiredService<ILabelRepository>().GetAsync(id, Ct))!;

    /// <summary>Vytvoří dvojici přes stránku nahrávání, takže existují skutečné bloby i entita.</summary>
    private static async Task<Label> SeedAsync(AppFactory factory, HttpClient client, (double lat, double lon)? gps = null)
    {
        var response = await client.PostFormAsync("/upload/",
            new Dictionary<string, string> { ["place"] = "Original", ["description"] = "orig", ["city"] = "Brno", ["country"] = "Česko" },
            new FormFile("men_image", "m.jpg", TestImages.Jpeg(200, 100, gps: gps)),
            new FormFile("women_image", "w.jpg", TestImages.Jpeg(100, 200)));
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        return Assert.Single(await factory.Services.GetRequiredService<ILabelRepository>().ListAsync(Ct));
    }

    private static async Task SetCountersAsync(AppFactory factory, string id, int voters, double avg)
    {
        var table = factory.Services.GetRequiredService<TableServiceClient>().GetTableClient(factory.LabelsTable);
        var patch = new TableEntity("label", id) { ["NumVoters"] = voters, ["AvgVote"] = avg };
        await table.UpdateEntityAsync(patch, ETag.All, TableUpdateMode.Merge, Ct);
    }

    [Fact]
    public async Task Edit_Anonymous_RedirectsToLogin()
    {
        await using var factory = new AppFactory(azurite);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/pair/abc/edit/", Ct);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        // Cookie middleware vrací absolutní URL (http://localhost/login?next=...), proto se kontroluje cesta.
        Assert.Equal("/login", response.Headers.Location!.AbsolutePath);
        Assert.Contains("next=%2Fpair%2Fabc%2Fedit%2F", response.Headers.Location.OriginalString);
    }

    [Fact]
    public async Task Edit_NonAdmin_Forbidden()
    {
        await using var factory = new AppFactory(azurite);
        var client = await factory.CreateUserClientAsync(isAdmin: false);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/pair/abc/edit/", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/pair/abc/edit/", new MultipartFormDataContent(), Ct)).StatusCode);
    }

    [Theory]
    [InlineData("/pair/does-not-exist/edit/")]
    [InlineData("/pair/a'b/edit/")]
    [InlineData("/pair/a%2Fb/edit/")]
    [InlineData("/pair/a%5Cb/edit")]
    public async Task Edit_UnknownId_Returns404(string url)
    {
        await using var factory = new AppFactory(azurite);
        var client = await factory.CreateAdminClientAsync();

        var response = await client.GetAsync(url, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Edit_UnknownId_PostReturns404()
    {
        var (factory, _) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        var client = await factory.CreateAdminClientAsync();
        var seeded = await SeedAsync(factory, client);
        // Token pochází ze stránky existující dvojice; tělo míří na neexistující.
        using var form = await client.BuildFormAsync($"/pair/{seeded.Id}/edit/", Fields());

        var response = await client.PostAsync("/pair/does-not-exist/edit/", form, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Edit_Get_ShowsCurrentValuesPreviewsAndPicker()
    {
        var (factory, _) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        var client = await factory.CreateAdminClientAsync();
        var seeded = await SeedAsync(factory, client, gps: (49.5, 16.5));

        var response = await client.GetAsync($"/pair/{seeded.Id}/edit/", Ct);
        var html = WebUtility.HtmlDecode(await response.BodyAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Edit Toilet Label", html);
        Assert.Contains("Save Changes", html);
        Assert.Contains("value=\"Original\"", html);
        Assert.Contains("value=\"Brno\"", html);
        Assert.Contains(">orig</textarea>", html);
        Assert.Contains($"src=\"http://blob.test/{seeded.Id}_men_thumb.webp\"", html);
        Assert.Contains($"src=\"http://blob.test/{seeded.Id}_women_thumb.webp\"", html);
        Assert.Matches(new Regex("<option[^>]*\\bselected\\b[^>]*>Česko</option>"), html);
        Assert.Contains("id=\"location-map\"", html);
        Assert.Contains("name=\"latitude\"", html);
        Assert.Contains("value=\"49.5\"", html);
        Assert.Contains("value=\"16.5\"", html);
        Assert.DoesNotContain(" required", html);
        Assert.True(html.IndexOf("men_image", StringComparison.Ordinal) < html.IndexOf("name=\"place\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Edit_Get_PreviewFallsBackToOriginalWithoutThumb()
    {
        var (factory, _) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        var client = await factory.CreateAdminClientAsync();
        await factory.Services.GetRequiredService<ILabelRepository>().CreateAsync(
            new Label { Id = "legacy-1", MenImage = "m.jpg" }, Ct);

        var html = await (await client.GetAsync("/pair/legacy-1/edit/", Ct)).BodyAsync();

        Assert.Contains("src=\"http://blob.test/m.jpg\"", html);
        Assert.DoesNotContain("alt=\"Women's sign\"", html);
    }

    [Fact]
    public async Task Edit_Get_ShowsLegacyCountryOption()
    {
        var (factory, _) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        var client = await factory.CreateAdminClientAsync();
        await factory.Services.GetRequiredService<ILabelRepository>().CreateAsync(
            new Label { Id = "legacy-1", Country = "Narnie", MenImage = "m.jpg" }, Ct);

        var html = await (await client.GetAsync("/pair/legacy-1/edit/", Ct)).BodyAsync();

        Assert.Matches(new Regex("<option[^>]*\\bselected\\b[^>]*>Narnie</option>"), html);
        Assert.Single(Regex.Matches(html, "<option[^>]*\\bselected\\b"));
    }

    [Fact]
    public async Task Edit_PreservesCreatedAndVotes()
    {
        var (factory, _) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        var client = await factory.CreateAdminClientAsync();
        var seeded = await SeedAsync(factory, client);
        await SetCountersAsync(factory, seeded.Id, 3, 4.5);

        var response = await client.PostFormAsync($"/pair/{seeded.Id}/edit/", Fields());

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
        var label = await GetLabelAsync(factory, seeded.Id);
        Assert.Equal(seeded.Created, label.Created);
        Assert.Equal(3, label.NumVoters);
        Assert.Equal(4.5, label.AvgVote);
        Assert.Equal("New place", label.Place);
        Assert.Equal("New description", label.Description);
        Assert.Equal("Německo", label.Country);
        Assert.Equal("Berlin", label.City);
    }

    [Fact]
    public async Task Edit_ManualCoordsWin()
    {
        var (factory, geocoder) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        var client = await factory.CreateAdminClientAsync();
        var seeded = await SeedAsync(factory, client, gps: (50, 14));

        await client.PostFormAsync($"/pair/{seeded.Id}/edit/", Fields(("latitude", "48.5"), ("longitude", "14.3")),
            new FormFile("men_image", "m.jpg", TestImages.Jpeg(100, 100, gps: (10, 20))));

        var label = await GetLabelAsync(factory, seeded.Id);
        Assert.Equal((48.5, 14.3), (label.Latitude, label.Longitude));
        Assert.Equal(0, geocoder.Calls);
    }

    [Fact]
    public async Task Edit_NewPhotoWithGps_UpdatesCoordinates()
    {
        var (factory, _) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        var client = await factory.CreateAdminClientAsync();
        var seeded = await SeedAsync(factory, client, gps: (50, 14));

        await client.PostFormAsync($"/pair/{seeded.Id}/edit/", Fields(),
            new FormFile("women_image", "w.jpg", TestImages.Jpeg(100, 100, gps: (10.5, 20.5))));

        var label = await GetLabelAsync(factory, seeded.Id);
        Assert.Equal((10.5, 20.5), (label.Latitude, label.Longitude));
    }

    [Fact]
    public async Task Edit_NewPhotoWithoutGps_KeepsCoords()
    {
        var (factory, geocoder) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        var client = await factory.CreateAdminClientAsync();
        var seeded = await SeedAsync(factory, client, gps: (50, 14));

        await client.PostFormAsync($"/pair/{seeded.Id}/edit/", Fields(),
            new FormFile("men_image", "m.jpg", TestImages.Jpeg(100, 100)));

        var label = await GetLabelAsync(factory, seeded.Id);
        Assert.Equal((50.0, 14.0), (label.Latitude, label.Longitude));
        Assert.Equal(0, geocoder.Calls);
    }

    [Fact]
    public async Task Edit_NoCoordinatesAnywhere_Geocodes()
    {
        var (factory, geocoder) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        var client = await factory.CreateAdminClientAsync();
        var seeded = await SeedAsync(factory, client);
        Assert.Null(seeded.Latitude);
        geocoder.Result = new GeoPoint(52.52, 13.405);

        await client.PostFormAsync($"/pair/{seeded.Id}/edit/", Fields());

        var label = await GetLabelAsync(factory, seeded.Id);
        Assert.Equal((52.52, 13.405), (label.Latitude, label.Longitude));
        Assert.Equal(2, geocoder.Calls); // jednou při seedu (bez výsledku), jednou při úpravě
    }

    [Fact]
    public async Task Edit_ChangedExtension_DeletesOldOriginal()
    {
        var (factory, _) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        var client = await factory.CreateAdminClientAsync();
        var seeded = await SeedAsync(factory, client);
        Assert.Equal($"{seeded.Id}_men.jpg", seeded.MenImage);

        var response = await client.PostFormAsync($"/pair/{seeded.Id}/edit/", Fields(),
            new FormFile("men_image", "new.png", TestImages.PngRgba(300, 300), "image/png"));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var label = await GetLabelAsync(factory, seeded.Id);
        Assert.Equal($"{seeded.Id}_men.png", label.MenImage);
        Assert.Equal($"{seeded.Id}_men_thumb.webp", label.MenThumb);
        var container = Container(factory);
        Assert.False(await container.GetBlobClient($"{seeded.Id}_men.jpg").ExistsAsync(Ct));
        Assert.True(await container.GetBlobClient($"{seeded.Id}_men.png").ExistsAsync(Ct));
        Assert.Equal("image/png", (await container.GetBlobClient($"{seeded.Id}_men.png").GetPropertiesAsync(cancellationToken: Ct)).Value.ContentType);
        // Ženská strana a její náhled zůstaly.
        Assert.Equal(
            new[] { $"{seeded.Id}_men.png", $"{seeded.Id}_men_thumb.webp", seeded.WomenImage, seeded.WomenThumb }.Order(StringComparer.Ordinal),
            await BlobNamesAsync(factory));
    }

    [Fact]
    public async Task Edit_SameExtension_OverwritesBlobs()
    {
        var (factory, _) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        var client = await factory.CreateAdminClientAsync();
        var seeded = await SeedAsync(factory, client);
        var before = (await Container(factory).GetBlobClient(seeded.WomenImage).GetPropertiesAsync(cancellationToken: Ct)).Value.ContentLength;

        await client.PostFormAsync($"/pair/{seeded.Id}/edit/", Fields(),
            new FormFile("women_image", "other.jpg", TestImages.Jpeg(600, 600)));

        var after = (await Container(factory).GetBlobClient(seeded.WomenImage).GetPropertiesAsync(cancellationToken: Ct)).Value.ContentLength;
        Assert.NotEqual(before, after);
        Assert.Equal(seeded.WomenImage, (await GetLabelAsync(factory, seeded.Id)).WomenImage);
        Assert.Equal(4, (await BlobNamesAsync(factory)).Count);
    }

    [Fact]
    public async Task Edit_NoNewFiles_KeepsImageNames()
    {
        var (factory, _) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        var client = await factory.CreateAdminClientAsync();
        var seeded = await SeedAsync(factory, client);
        var blobsBefore = await BlobNamesAsync(factory);

        // Prázdná část souboru, jak ji posílá prohlížeč bez vybraného souboru.
        await client.PostFormAsync($"/pair/{seeded.Id}/edit/", Fields(),
            new FormFile("men_image", "", []), new FormFile("women_image", "", []));

        var label = await GetLabelAsync(factory, seeded.Id);
        Assert.Equal(seeded.MenImage, label.MenImage);
        Assert.Equal(seeded.WomenImage, label.WomenImage);
        Assert.Equal(seeded.MenThumb, label.MenThumb);
        Assert.Equal(seeded.WomenThumb, label.WomenThumb);
        Assert.Equal(blobsBefore, await BlobNamesAsync(factory));
    }

    [Fact]
    public async Task Edit_NotAnImage_ShowsErrorAndChangesNothing()
    {
        var (factory, _) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        var client = await factory.CreateAdminClientAsync();
        var seeded = await SeedAsync(factory, client);
        var blobsBefore = await BlobNamesAsync(factory);

        var response = await client.PostFormAsync($"/pair/{seeded.Id}/edit/", Fields(),
            new FormFile("men_image", "m.txt", "nope"u8.ToArray(), "text/plain"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.BodyAsync());
        Assert.Contains("Soubor není obrázek.", html);
        Assert.Contains("value=\"New place\"", html);
        Assert.Equal(seeded, await GetLabelAsync(factory, seeded.Id));
        Assert.Equal(blobsBefore, await BlobNamesAsync(factory));
    }

    [Fact]
    public async Task Edit_TextTooLong_ShowsErrorAndChangesNothing()
    {
        var (factory, _) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        var client = await factory.CreateAdminClientAsync();
        var seeded = await SeedAsync(factory, client);

        var response = await client.PostFormAsync($"/pair/{seeded.Id}/edit/", Fields(("description", new string('x', 2001))),
            new FormFile("men_image", "m.png", TestImages.PngRgba(50, 50), "image/png"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Text je příliš dlouhý.", WebUtility.HtmlDecode(await response.BodyAsync()));
        Assert.Equal(seeded, await GetLabelAsync(factory, seeded.Id));
        Assert.False(await Container(factory).GetBlobClient($"{seeded.Id}_men.png").ExistsAsync(Ct));
    }

    /// <summary>Starý záznam s textem nad limitem (např. z dřívější verze) uloží přímo do tabulky.</summary>
    private static async Task<Label> SeedLongPlaceAsync(AppFactory factory, HttpClient client, int length)
    {
        var seeded = await SeedAsync(factory, client);
        var table = factory.Services.GetRequiredService<TableServiceClient>().GetTableClient(factory.LabelsTable);
        var patch = new TableEntity("label", seeded.Id) { ["Place"] = new string('p', length) };
        await table.UpdateEntityAsync(patch, ETag.All, TableUpdateMode.Merge, Ct);
        return await GetLabelAsync(factory, seeded.Id);
    }

    [Fact]
    public async Task Edit_LegacyTooLongPlace_CanBeEditedWhenPlaceUnchanged()
    {
        var (factory, _) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        var client = await factory.CreateAdminClientAsync();
        var legacy = await SeedLongPlaceAsync(factory, client, 300);

        var response = await client.PostFormAsync($"/pair/{legacy.Id}/edit/", Fields(("place", legacy.Place), ("city", "Plzeň")));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var label = await GetLabelAsync(factory, legacy.Id);
        Assert.Equal(legacy.Place, label.Place);
        Assert.Equal("Plzeň", label.City);
    }

    [Fact]
    public async Task Edit_LegacyTooLongPlace_ChangedToAnotherTooLongValue_Errors()
    {
        var (factory, _) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        var client = await factory.CreateAdminClientAsync();
        var legacy = await SeedLongPlaceAsync(factory, client, 300);

        var response = await client.PostFormAsync($"/pair/{legacy.Id}/edit/", Fields(("place", new string('q', 250))));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Text je příliš dlouhý.", WebUtility.HtmlDecode(await response.BodyAsync()));
        Assert.Equal(legacy, await GetLabelAsync(factory, legacy.Id));
    }

    [Fact]
    public async Task Edit_LegacyTooLongDescription_BrowserCrlfLineBreaksCountAsUnchanged()
    {
        var (factory, _) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        var client = await factory.CreateAdminClientAsync();
        var seeded = await SeedAsync(factory, client);
        var stored = string.Join("\n", Enumerable.Repeat(new string('d', 99), 25));   // 2 499 znaků, nad limitem 2 000
        var table = factory.Services.GetRequiredService<TableServiceClient>().GetTableClient(factory.LabelsTable);
        await table.UpdateEntityAsync(new TableEntity("label", seeded.Id) { ["Description"] = stored }, ETag.All, TableUpdateMode.Merge, Ct);

        var response = await client.PostFormAsync($"/pair/{seeded.Id}/edit/",
            Fields(("description", stored.Replace("\n", "\r\n"))));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }

    [Fact]
    public async Task Edit_OverLimit_Returns413()
    {
        var (factory, _) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        var client = await factory.CreateAdminClientAsync();
        var seeded = await SeedAsync(factory, client);

        var response = await client.PostFormAsync($"/pair/{seeded.Id}/edit/", Fields(),
            new FormFile("men_image", "m.jpg", new byte[52_428_800 + 1]));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(seeded, await GetLabelAsync(factory, seeded.Id));
    }
}
