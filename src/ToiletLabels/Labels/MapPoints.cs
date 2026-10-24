using System.Text.Json.Serialization;

namespace ToiletLabels.Labels;

/// <summary>
/// Značka na mapě. Jména ve JSON zůstávají stejná jako v Pythonu (<c>lat</c>, <c>men_url</c> …),
/// protože je čte převzaté JavaScript.
/// </summary>
public sealed record MapPoint(
    [property: JsonPropertyName("lat")] double Lat,
    [property: JsonPropertyName("lon")] double Lon,
    [property: JsonPropertyName("place")] string Place,
    [property: JsonPropertyName("men_url")] string MenUrl,
    [property: JsonPropertyName("women_url")] string WomenUrl,
    [property: JsonPropertyName("row_key")] string RowKey);

/// <summary>Port <c>_build_map_points</c> z <c>gallery/views.py</c>.</summary>
public static class MapPoints
{
    /// <summary>Značky jen pro štítky, které mají obě souřadnice. Obrázek je náhled, jinak originál.</summary>
    public static IReadOnlyList<MapPoint> Build(IEnumerable<Label> labels, string blobBaseUrl) =>
        labels
            .Where(l => l.Latitude is not null && l.Longitude is not null)
            .Select(l => new MapPoint(
                l.Latitude!.Value,
                l.Longitude!.Value,
                l.Title,
                BlobUrl(blobBaseUrl, Preferred(l.MenThumb, l.MenImage)),
                BlobUrl(blobBaseUrl, Preferred(l.WomenThumb, l.WomenImage)),
                l.Id))
            .ToList();

    /// <summary>Název blobu k zobrazení: náhled, a když chybí, originál.</summary>
    public static string Preferred(string thumb, string original) =>
        !string.IsNullOrEmpty(thumb) ? thumb : original;

    private static string BlobUrl(string baseUrl, string blobName) =>
        blobName.Length == 0 ? "" : baseUrl + blobName;
}
