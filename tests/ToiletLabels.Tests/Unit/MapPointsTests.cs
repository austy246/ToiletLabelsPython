using System.Text.Json;
using ToiletLabels.Labels;

namespace ToiletLabels.Tests.Unit;

public class MapPointsTests
{
    private const string BaseUrl = "http://blob.test/";

    private static Label Make(string id, double? lat = 50.1, double? lon = 14.2) =>
        new() { Id = id, Latitude = lat, Longitude = lon };

    [Fact]
    public void Build_OnlyLabelsWithCoords()
    {
        var labels = new[]
        {
            Make("both") with { Place = "Cafe", MenImage = "m.jpg", WomenImage = "w.jpg" },
            Make("noLat", lat: null),
            Make("noLon", lon: null),
            Make("none", lat: null, lon: null),
        };

        var points = MapPoints.Build(labels, BaseUrl);

        var point = Assert.Single(points);
        Assert.Equal("both", point.RowKey);
        Assert.Equal(50.1, point.Lat);
        Assert.Equal(14.2, point.Lon);
        Assert.Equal("Cafe", point.Place);
        Assert.Equal("http://blob.test/m.jpg", point.MenUrl);
        Assert.Equal("http://blob.test/w.jpg", point.WomenUrl);
    }

    [Fact]
    public void Build_PlaceFallsBackToCityThenUntitled()
    {
        var labels = new[]
        {
            Make("a") with { Place = "", City = "Berlin" },
            Make("b") with { Place = "  ", City = "" },
        };

        var points = MapPoints.Build(labels, BaseUrl);

        Assert.Equal(["Berlin", "Untitled"], points.Select(p => p.Place));
        Assert.Equal("", points[0].MenUrl);
        Assert.Equal("", points[0].WomenUrl);
    }

    [Fact]
    public void Build_PrefersThumbnail()
    {
        var labels = new[]
        {
            Make("withThumb") with
            {
                MenImage = "a_men.jpg", MenThumb = "a_men_thumb.webp",
                WomenImage = "a_women.jpg", WomenThumb = "a_women_thumb.webp",
            },
            Make("noThumb") with { MenImage = "b_men.jpg", WomenImage = "b_women.jpg" },
        };

        var points = MapPoints.Build(labels, BaseUrl);

        Assert.Equal("http://blob.test/a_men_thumb.webp", points[0].MenUrl);
        Assert.Equal("http://blob.test/a_women_thumb.webp", points[0].WomenUrl);
        Assert.Equal("http://blob.test/b_men.jpg", points[1].MenUrl);
        Assert.Equal("http://blob.test/b_women.jpg", points[1].WomenUrl);
    }

    [Fact]
    public void Serialize_UsesSnakeCaseNamesReadByTheScript()
    {
        var point = new MapPoint(50.1, 14.2, "Cafe", "m", "w", "id1");

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(point));

        Assert.Equal(
            ["lat", "lon", "place", "men_url", "women_url", "row_key"],
            doc.RootElement.EnumerateObject().Select(p => p.Name));
    }
}
