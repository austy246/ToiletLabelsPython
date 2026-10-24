using System.Net;
using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using ToiletLabels.Labels;

namespace ToiletLabels.Tests.Integration;

public class GalleryPageTests(AzuriteFixture azurite)
{
    private static Label Pair(string id = "id1") => new()
    {
        Id = id,
        Place = "Cafe",
        MenImage = "m.jpg",
        WomenImage = "w.jpg",
        Created = "2026-03-04T10:00:00.000000",
    };

    private static async Task AddAsync(AppFactory factory, params Label[] labels)
    {
        var repo = factory.Services.GetRequiredService<ILabelRepository>();
        foreach (var label in labels)
            await repo.CreateAsync(label, TestContext.Current.CancellationToken);
    }

    private static async Task<string> GetHtmlAsync(HttpClient client)
    {
        var response = await client.GetAsync("/", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Index_RendersMapWhenPointsAndKey()
    {
        await using var factory = new AppFactory(azurite);
        await AddAsync(factory, Pair() with { Latitude = 50.1, Longitude = 14.2 });

        var html = await GetHtmlAsync(factory.CreateClient());

        Assert.Contains("id=\"map\"", html);
        Assert.Contains("id=\"map-points-data\"", html);
        Assert.Contains("api.mapy.com/v1/maptiles/basic/256", html);
        Assert.Contains("\"test-key\"", html);
        Assert.Contains("id=\"pair-id1\"", html);
        Assert.Contains("toilet-marker", html);
        Assert.Contains("viewBox=\"0 0 448 512\"", html);
        Assert.Contains("\"row_key\":\"id1\"", html);
        Assert.Contains("Zobrazit kartu", html);
    }

    [Fact]
    public async Task Index_HidesMapWithoutKey()
    {
        await using var factory = new AppFactory(azurite).WithSetting("Mapy:ApiKey", "");
        await AddAsync(factory, Pair() with { Latitude = 50.1, Longitude = 14.2 });

        var html = await GetHtmlAsync(factory.CreateClient());

        Assert.DoesNotContain("map-points-data", html);
        Assert.DoesNotContain("api.mapy.com", html);
        Assert.Contains("id=\"pair-id1\"", html);
    }

    [Fact]
    public async Task Index_HidesMapWithoutPoints()
    {
        await using var factory = new AppFactory(azurite);
        await AddAsync(factory, Pair());

        var html = await GetHtmlAsync(factory.CreateClient());

        Assert.DoesNotContain("map-points-data", html);
        Assert.DoesNotContain("id=\"map\"", html);
    }

    [Fact]
    public async Task Index_CardUsesThumbnailWithFallback()
    {
        await using var factory = new AppFactory(azurite);
        await AddAsync(factory, Pair() with { MenThumb = "m_thumb.webp" });

        var html = await GetHtmlAsync(factory.CreateClient());

        // Muži mají náhled, ženy ne, takže použijí originál.
        Assert.Contains("http://blob.test/m_thumb.webp", html);
        Assert.Contains("http://blob.test/w.jpg", html);
        Assert.DoesNotContain("http://blob.test/m.jpg", html);
    }

    [Fact]
    public async Task Index_ViewOnMapLinksToMapy()
    {
        await using var factory = new AppFactory(azurite);
        await AddAsync(factory, Pair() with { Latitude = 50.08, Longitude = 14.42 });

        var html = await GetHtmlAsync(factory.CreateClient());

        Assert.Contains("mapy.com/fnc/v1/showmap?center=14.42,50.08&zoom=17&marker=true", html);
        Assert.Contains("View on map", html);
        Assert.DoesNotContain("maps.google.com", html);
    }

    [Fact]
    public async Task Index_ShowsFlagForKnownCountry()
    {
        await using var factory = new AppFactory(azurite);
        await AddAsync(factory, Pair() with { Country = "Česko" });

        var html = await GetHtmlAsync(factory.CreateClient());

        Assert.Contains("https://flagcdn.com/24x18/cz.png", html);
        Assert.Contains("https://flagcdn.com/48x36/cz.png 2x", html);
    }

    [Fact]
    public async Task Index_UnknownCountry_ShowsFlagIconInsteadOfImage()
    {
        await using var factory = new AppFactory(azurite);
        await AddAsync(factory, Pair() with { Country = "Atlantis" });

        var html = await GetHtmlAsync(factory.CreateClient());

        Assert.DoesNotContain("flagcdn.com", html);
        Assert.Contains("Atlantis", html);
    }

    [Fact]
    public async Task Index_EscapesPlaceInJson()
    {
        await using var factory = new AppFactory(azurite);
        await AddAsync(factory, Pair() with { Place = "</script><b>", Latitude = 50.1, Longitude = 14.2 });

        var html = await GetHtmlAsync(factory.CreateClient());

        Assert.Contains("map-points-data", html);
        Assert.DoesNotContain("</script><b>", html);
    }

    [Fact]
    public async Task Index_EmptyState()
    {
        await using var factory = new AppFactory(azurite);

        var html = await GetHtmlAsync(factory.CreateClient());

        Assert.Contains("No signs yet", html);
        Assert.DoesNotContain("<article", html);
    }

    [Fact]
    public async Task Index_RendersCardDetails()
    {
        await using var factory = new AppFactory(azurite);
        await AddAsync(factory, Pair() with
        {
            Place = "Kavárna", Description = "Popis", City = "Praha", NumVoters = 3, AvgVote = 4,
        });

        // Razor kóduje znaky mimo ASCII jako entity, prohlížeč je zobrazí normálně.
        var html = WebUtility.HtmlDecode(await GetHtmlAsync(factory.CreateClient()));

        Assert.Contains("Kavárna", html);
        Assert.Contains("Popis", html);
        Assert.Contains("Praha", html);
        Assert.Contains("3 votes", html);
        Assert.Contains("2026-03-04", html);
        Assert.DoesNotContain("2026-03-04T", html);
    }

    [Fact]
    public async Task Index_EditButtonOnlyForAdmin()
    {
        await using var factory = new AppFactory(azurite);
        await AddAsync(factory, Pair());

        var anonymous = await GetHtmlAsync(factory.CreateClient());
        var user = await GetHtmlAsync(await factory.CreateUserClientAsync(isAdmin: false));
        var admin = await GetHtmlAsync(await factory.CreateAdminClientAsync());

        Assert.DoesNotContain("/pair/id1/edit/", anonymous);
        Assert.DoesNotContain("/pair/id1/edit/", user);
        Assert.Contains("/pair/id1/edit/", admin);
    }

    [Fact]
    public async Task Index_HeaderShowsLoginForAnonymousAndLogoutFormForUsers()
    {
        await using var factory = new AppFactory(azurite);

        var anonymous = await GetHtmlAsync(factory.CreateClient());
        var user = await GetHtmlAsync(await factory.CreateUserClientAsync(isAdmin: false));

        Assert.Contains("href=\"/login/\"", anonymous);
        Assert.DoesNotContain("action=\"/logout/\"", anonymous);
        Assert.Contains("action=\"/logout/\"", user);
        Assert.Contains("__RequestVerificationToken", user);
        Assert.DoesNotContain("href=\"/login/\"", user);
    }

    [Fact]
    public async Task Index_LegacyEntitiesRender()
    {
        await using var factory = new AppFactory(azurite);
        var idA = Guid.NewGuid().ToString();
        var idB = Guid.NewGuid().ToString();
        var table = factory.Services.GetRequiredService<TableServiceClient>().GetTableClient(factory.LabelsTable);
        await table.AddEntityAsync(LegacyEntities.A(idA), TestContext.Current.CancellationToken);
        await table.AddEntityAsync(LegacyEntities.B(idB), TestContext.Current.CancellationToken);

        var html = await GetHtmlAsync(factory.CreateClient());

        Assert.Contains($"id=\"pair-{idA}\"", html);
        Assert.Contains($"id=\"pair-{idB}\"", html);
        Assert.Contains($"\"row_key\":\"{idB}\"", html);
        Assert.DoesNotContain($"\"row_key\":\"{idA}\"", html);
    }
}
