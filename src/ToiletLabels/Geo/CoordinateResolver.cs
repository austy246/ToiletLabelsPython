namespace ToiletLabels.Geo;

/// <summary>
/// Výběr souřadnic štítku. Ruční souřadnice řeší volající před voláním resolveru.
/// </summary>
public static class CoordinateResolver
{
    /// <summary>Nový štítek: GPS z fotky pro muže, GPS z fotky pro ženy, geokódování.</summary>
    public static async Task<GeoPoint?> ResolveForCreateAsync(
        GeoPoint? menGps, GeoPoint? womenGps,
        string place, string city, string country,
        IGeocoder geocoder, CancellationToken ct)
    {
        return menGps ?? womenGps ?? await geocoder.GeocodeAsync(place, city, country, ct);
    }

    /// <summary>
    /// Úprava štítku: GPS z nově nahraných fotek, existující souřadnice, geokódování.
    /// Nová fotka bez GPS existující souřadnice nemaže.
    /// </summary>
    public static async Task<GeoPoint?> ResolveForEditAsync(
        GeoPoint? newMenGps, GeoPoint? newWomenGps, GeoPoint? existing,
        string place, string city, string country,
        IGeocoder geocoder, CancellationToken ct)
    {
        return newMenGps ?? newWomenGps ?? existing ?? await geocoder.GeocodeAsync(place, city, country, ct);
    }
}
