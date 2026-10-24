using ToiletLabels.Geo;

namespace ToiletLabels.Tests;

/// <summary>Geokodér bez sítě: vrací nastavený bod a počítá volání.</summary>
public sealed class FakeGeocoder : IGeocoder
{
    private int _calls;

    public GeoPoint? Result { get; set; }

    public int Calls => _calls;

    public Task<GeoPoint?> GeocodeAsync(string place, string city, string country, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(Result);
    }
}
