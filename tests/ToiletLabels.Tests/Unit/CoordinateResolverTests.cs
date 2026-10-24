using ToiletLabels.Geo;

namespace ToiletLabels.Tests.Unit;

public class CoordinateResolverTests
{
    private sealed class FakeGeocoder(GeoPoint? result) : IGeocoder
    {
        public int Calls { get; private set; }

        public Task<GeoPoint?> GeocodeAsync(string place, string city, string country, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private static readonly GeoPoint Men = new(50, 14);
    private static readonly GeoPoint Women = new(51, 15);
    private static readonly GeoPoint Geocoded = new(52, 16);
    private static readonly GeoPoint Existing = new(53, 17);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_PrefersMenGps()
    {
        var geocoder = new FakeGeocoder(Geocoded);
        var result = await CoordinateResolver.ResolveForCreateAsync(Men, Women, "p", "c", "k", geocoder, Ct);
        Assert.Equal(Men, result);
        Assert.Equal(0, geocoder.Calls);
    }

    [Fact]
    public async Task Create_FallsBackToWomenThenGeocode()
    {
        var geocoder = new FakeGeocoder(Geocoded);

        Assert.Equal(Women, await CoordinateResolver.ResolveForCreateAsync(null, Women, "p", "c", "k", geocoder, Ct));
        Assert.Equal(0, geocoder.Calls);

        Assert.Equal(Geocoded, await CoordinateResolver.ResolveForCreateAsync(null, null, "p", "c", "k", geocoder, Ct));
        Assert.Equal(1, geocoder.Calls);
    }

    [Fact]
    public async Task Create_NothingFound_Null()
    {
        var geocoder = new FakeGeocoder(null);
        Assert.Null(await CoordinateResolver.ResolveForCreateAsync(null, null, "p", "c", "k", geocoder, Ct));
        Assert.Equal(1, geocoder.Calls);
    }

    [Fact]
    public async Task Edit_NewPhotoWithoutGps_KeepsExisting()
    {
        var geocoder = new FakeGeocoder(Geocoded);
        var result = await CoordinateResolver.ResolveForEditAsync(null, null, new GeoPoint(50, 14), "p", "c", "k", geocoder, Ct);
        Assert.Equal(new GeoPoint(50, 14), result);
        Assert.Equal(0, geocoder.Calls);
    }

    [Fact]
    public async Task Edit_NewPhotoGps_Overrides()
    {
        var geocoder = new FakeGeocoder(Geocoded);

        Assert.Equal(Men, await CoordinateResolver.ResolveForEditAsync(Men, Women, Existing, "p", "c", "k", geocoder, Ct));
        Assert.Equal(Women, await CoordinateResolver.ResolveForEditAsync(null, Women, Existing, "p", "c", "k", geocoder, Ct));
        Assert.Equal(0, geocoder.Calls);
    }

    [Fact]
    public async Task Edit_NoExisting_Geocodes()
    {
        var geocoder = new FakeGeocoder(Geocoded);
        var result = await CoordinateResolver.ResolveForEditAsync(null, null, null, "p", "c", "k", geocoder, Ct);
        Assert.Equal(Geocoded, result);
        Assert.Equal(1, geocoder.Calls);
    }
}
