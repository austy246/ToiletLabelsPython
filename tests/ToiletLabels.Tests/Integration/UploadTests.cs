using System.Net;
using Azure.Storage.Blobs;
using ImageMagick;
using Microsoft.Extensions.DependencyInjection;
using ToiletLabels.Geo;
using ToiletLabels.Labels;
using ToiletLabels.Tests.Infrastructure;

namespace ToiletLabels.Tests.Integration;

public class UploadTests(AzuriteFixture azurite)
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
            ["place"] = "Cafe",
            ["description"] = "desc",
            ["country"] = "",
            ["city"] = "",
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

    private static Task<IReadOnlyList<Label>> LabelsAsync(AppFactory factory) =>
        factory.Services.GetRequiredService<ILabelRepository>().ListAsync(Ct);

    private static byte[] Bmp()
    {
        using var image = new MagickImage(MagickColors.Red, 10, 10);
        image.Format = MagickFormat.Bmp;
        return image.ToByteArray();
    }

    [Fact]
    public async Task Upload_Anonymous_RedirectsToLogin()
    {
        await using var factory = new AppFactory(azurite);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/upload/", Ct);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        // Cookie middleware vrací absolutní URL (http://localhost/login?next=...), proto se kontroluje cesta.
        Assert.Equal("/login", response.Headers.Location!.AbsolutePath);
        Assert.Contains("next=%2Fupload%2F", location);
    }

    [Fact]
    public async Task Upload_NonAdmin_Forbidden()
    {
        await using var factory = new AppFactory(azurite);
        var client = await factory.CreateUserClientAsync(isAdmin: false);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/upload/", Ct)).StatusCode);
        var post = await client.PostAsync("/upload/", new MultipartFormDataContent(), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
    }

    [Fact]
    public async Task Upload_Get_RendersFormImagesFirst()
    {
        await using var factory = new AppFactory(azurite);
        var client = await factory.CreateAdminClientAsync();

        foreach (var url in new[] { "/upload/", "/upload" })
        {
            var response = await client.GetAsync(url, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.BodyAsync();

            Assert.True(html.IndexOf("men_image", StringComparison.Ordinal) < html.IndexOf("name=\"place\"", StringComparison.Ordinal));
            Assert.Contains("id=\"location-map\"", html);
            Assert.Contains("https://cdn.jsdelivr.net/npm/exifr@7.1.3/dist/full.umd.js", html);
            Assert.Contains("id=\"geocode-search\"", html);
            Assert.Contains("enctype=\"multipart/form-data\"", html);
            Assert.Contains("__RequestVerificationToken", html);
            Assert.Contains("— vyber zemi —", System.Net.WebUtility.HtmlDecode(html));
            Assert.Contains("ERR_UPLOAD_FILE_CHANGED", html);
        }
    }

    [Fact]
    public async Task Upload_Get_WithoutMapyKey_OmitsPicker()
    {
        await using var factory = new AppFactory(azurite).WithSetting("Mapy:ApiKey", "");
        var client = await factory.CreateAdminClientAsync();

        var html = await (await client.GetAsync("/upload/", Ct)).BodyAsync();

        Assert.DoesNotContain("location-map", html);
        Assert.DoesNotContain("name=\"latitude\"", html);
        Assert.Contains("name=\"men_image\"", html);
    }

    [Fact]
    public async Task Upload_StoresOriginalsThumbsAndEntity()
    {
        var (factory, geocoder) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        var client = await factory.CreateAdminClientAsync();

        var response = await client.PostFormAsync("/upload/", Fields(("country", "Slovensko"), ("city", "Brno")),
            new FormFile("men_image", "m.jpg", TestImages.Jpeg(800, 600, gps: (49.195061, 16.606836))),
            new FormFile("women_image", "w.jpg", TestImages.Jpeg(300, 300)));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
        var label = Assert.Single(await LabelsAsync(factory));
        Assert.Equal($"{label.Id}_men.jpg", label.MenImage);
        Assert.Equal($"{label.Id}_men_thumb.webp", label.MenThumb);
        Assert.Equal($"{label.Id}_women_thumb.webp", label.WomenThumb);
        Assert.Equal(49.195061, label.Latitude);
        Assert.Equal(16.606836, label.Longitude);
        Assert.Equal(0, label.NumVoters);
        Assert.Equal("Cafe", label.Place);
        Assert.Equal("Slovensko", label.Country);
        Assert.Equal("Brno", label.City);
        Assert.Equal(0, geocoder.Calls);

        Assert.Equal(
            new[] { label.MenImage, label.MenThumb, label.WomenImage, label.WomenThumb }.Order(StringComparer.Ordinal),
            await BlobNamesAsync(factory));
        var container = Container(factory);
        Assert.Equal("image/jpeg", (await container.GetBlobClient(label.MenImage).GetPropertiesAsync(cancellationToken: Ct)).Value.ContentType);
        Assert.Equal("image/webp", (await container.GetBlobClient(label.MenThumb).GetPropertiesAsync(cancellationToken: Ct)).Value.ContentType);
        Assert.Equal("image/webp", (await container.GetBlobClient(label.WomenThumb).GetPropertiesAsync(cancellationToken: Ct)).Value.ContentType);
    }

    [Fact]
    public async Task Upload_ManualCoordsWin()
    {
        var (factory, geocoder) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        geocoder.Result = new GeoPoint(1, 2);
        var client = await factory.CreateAdminClientAsync();

        var response = await client.PostFormAsync("/upload/", Fields(("latitude", "49.1"), ("longitude", "16.6")),
            new FormFile("men_image", "m.jpg", TestImages.Jpeg(100, 100, gps: (10, 20))),
            new FormFile("women_image", "w.jpg", TestImages.Jpeg(100, 100)));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var label = Assert.Single(await LabelsAsync(factory));
        Assert.Equal(49.1, label.Latitude);
        Assert.Equal(16.6, label.Longitude);
        Assert.Equal(0, geocoder.Calls);
    }

    [Fact]
    public async Task Upload_NoGps_FallsBackToGeocoder()
    {
        var (factory, geocoder) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        geocoder.Result = new GeoPoint(50.08, 14.42);
        var client = await factory.CreateAdminClientAsync();

        await client.PostFormAsync("/upload/", Fields(("city", "Praha")),
            new FormFile("men_image", "m.jpg", TestImages.Jpeg(100, 100)),
            new FormFile("women_image", "w.jpg", TestImages.Jpeg(100, 100)));

        var label = Assert.Single(await LabelsAsync(factory));
        Assert.Equal((50.08, 14.42), (label.Latitude, label.Longitude));
        Assert.Equal(1, geocoder.Calls);
    }

    [Fact]
    public async Task Upload_NormalizesCountry()
    {
        var (factory, _) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        var client = await factory.CreateAdminClientAsync();

        await client.PostFormAsync("/upload/", Fields(("country", "czech republic")),
            new FormFile("men_image", "m.jpg", TestImages.Jpeg(100, 100)),
            new FormFile("women_image", "w.jpg", TestImages.Jpeg(100, 100)));

        Assert.Equal("Česko", Assert.Single(await LabelsAsync(factory)).Country);
    }

    [Fact]
    public async Task Upload_MissingWomenImage_ShowsError()
    {
        var (factory, _) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        var client = await factory.CreateAdminClientAsync();

        var response = await client.PostFormAsync("/upload/", Fields(),
            new FormFile("men_image", "m.jpg", TestImages.Jpeg(100, 100)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Nahraj fotku – ženy.", System.Net.WebUtility.HtmlDecode(await response.BodyAsync()));
        Assert.Empty(await LabelsAsync(factory));
        Assert.Empty(await BlobNamesAsync(factory));
    }

    [Fact]
    public async Task Upload_MissingMenImage_ShowsErrorAndKeepsFields()
    {
        var (factory, _) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        var client = await factory.CreateAdminClientAsync();

        var response = await client.PostFormAsync("/upload/", Fields(("place", "Kavárna <b>"), ("latitude", "49.5"), ("longitude", "16.5")),
            new FormFile("women_image", "w.jpg", TestImages.Jpeg(100, 100)));

        var raw = await response.BodyAsync();
        var html = System.Net.WebUtility.HtmlDecode(raw);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Nahraj fotku – muži.", html);
        Assert.DoesNotContain("Nahraj fotku – ženy.", html);
        Assert.Contains("value=\"Kavárna <b>\"", html);
        Assert.DoesNotContain("Kavárna <b>", raw);
        Assert.Contains("name=\"latitude\"", html);
        Assert.Contains("value=\"49.5\"", html);
        Assert.Empty(await LabelsAsync(factory));
    }

    [Fact]
    public async Task Upload_NotAnImage_ShowsError()
    {
        var (factory, _) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        var client = await factory.CreateAdminClientAsync();

        var response = await client.PostFormAsync("/upload/", Fields(),
            new FormFile("men_image", "m.jpg", TestImages.Jpeg(100, 100)),
            new FormFile("women_image", "w.txt", "not an image"u8.ToArray(), "text/plain"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Soubor není obrázek.", System.Net.WebUtility.HtmlDecode(await response.BodyAsync()));
        Assert.Empty(await LabelsAsync(factory));
        Assert.Empty(await BlobNamesAsync(factory));
    }

    [Fact]
    public async Task Upload_FormatMagickReadsButSiteDoesNotServe_ShowsError()
    {
        var (factory, _) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        var client = await factory.CreateAdminClientAsync();

        var response = await client.PostFormAsync("/upload/", Fields(),
            new FormFile("men_image", "m.bmp", Bmp(), "image/bmp"),
            new FormFile("women_image", "w.jpg", TestImages.Jpeg(100, 100)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Soubor není obrázek.", System.Net.WebUtility.HtmlDecode(await response.BodyAsync()));
        Assert.Empty(await BlobNamesAsync(factory));
    }

    [Theory]
    [InlineData("place", 201)]
    [InlineData("description", 2001)]
    public async Task Upload_TextTooLong_ShowsError(string field, int length)
    {
        var (factory, _) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        var client = await factory.CreateAdminClientAsync();

        var response = await client.PostFormAsync("/upload/", Fields((field, new string('x', length))),
            new FormFile("men_image", "m.jpg", TestImages.Jpeg(100, 100)),
            new FormFile("women_image", "w.jpg", TestImages.Jpeg(100, 100)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Text je příliš dlouhé.", System.Net.WebUtility.HtmlDecode(await response.BodyAsync()));
        Assert.Empty(await LabelsAsync(factory));
        Assert.Empty(await BlobNamesAsync(factory));
    }

    [Fact]
    public async Task Upload_TextAtLimit_IsAccepted()
    {
        var (factory, _) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        var client = await factory.CreateAdminClientAsync();

        var response = await client.PostFormAsync("/upload/",
            Fields(("place", new string('p', 200)), ("description", new string('d', 2000))),
            new FormFile("men_image", "m.jpg", TestImages.Jpeg(100, 100)),
            new FormFile("women_image", "w.jpg", TestImages.Jpeg(100, 100)));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Single(await LabelsAsync(factory));
    }

    [Fact]
    public async Task Upload_Heic_Works()
    {
        var (factory, _) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        var client = await factory.CreateAdminClientAsync();
        var heic = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.heic"), Ct);

        var response = await client.PostFormAsync("/upload/", Fields(),
            new FormFile("men_image", "IMG_0001.HEIC", heic, "image/heic"),
            new FormFile("women_image", "w.jpg", TestImages.Jpeg(100, 100)));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var label = Assert.Single(await LabelsAsync(factory));
        Assert.Equal($"{label.Id}_men.HEIC", label.MenImage);
        var container = Container(factory);
        Assert.Equal("image/heic", (await container.GetBlobClient(label.MenImage).GetPropertiesAsync(cancellationToken: Ct)).Value.ContentType);
        Assert.NotEqual("", label.MenThumb);
        Assert.True(await container.GetBlobClient(label.MenThumb).ExistsAsync(Ct));
    }

    [Fact]
    public async Task Upload_WithoutAntiforgeryToken_IsRejected()
    {
        var (factory, _) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        var client = await factory.CreateAdminClientAsync();
        using var form = new MultipartFormDataContent
        {
            { new StringContent("Cafe"), "place" },
            { new ByteArrayContent(TestImages.Jpeg(50, 50)), "men_image", "m.jpg" },
            { new ByteArrayContent(TestImages.Jpeg(50, 50)), "women_image", "w.jpg" },
        };

        var response = await client.PostAsync("/upload/", form, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await LabelsAsync(factory));
        Assert.Empty(await BlobNamesAsync(factory));
    }

    [Fact]
    public async Task Upload_OverLimit_Returns413()
    {
        var (factory, _) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        var client = await factory.CreateAdminClientAsync();

        var response = await client.PostFormAsync("/upload/", Fields(),
            new FormFile("men_image", "m.jpg", new byte[52_428_800 + 1]),
            new FormFile("women_image", "w.jpg", TestImages.Jpeg(50, 50)));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(await LabelsAsync(factory));
        Assert.Empty(await BlobNamesAsync(factory));
    }

    [Fact]
    public async Task Upload_OverLimitWithoutContentLength_Returns413()
    {
        var (factory, _) = CreateFactory(azurite);
        await using var disposeFactory = factory;
        var client = await factory.CreateAdminClientAsync();
        var token = await client.GetAntiforgeryTokenAsync("/upload/", Ct);
        using var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new UnknownLengthContent(new byte[52_428_800 + 1]), "men_image", "m.jpg" },
        };

        var response = await client.PostAsync("/upload/", form, Ct);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(await LabelsAsync(factory));
    }
}
