using System.Globalization;
using System.Text.Json;

namespace ToiletLabels.Geo;

/// <summary>Převod názvu místa na souřadnice.</summary>
public interface IGeocoder
{
    /// <summary>Vrátí souřadnice pro „místo, město, země“, nebo <c>null</c> (nic nenalezeno, chyba, prázdný dotaz).</summary>
    Task<GeoPoint?> GeocodeAsync(string place, string city, string country, CancellationToken ct);
}

/// <summary>Geokodér nad Nominatim (OpenStreetMap). Jakákoli chyba vede na <c>null</c>.</summary>
public sealed class NominatimGeocoder(HttpClient http) : IGeocoder
{
    private const string SearchUrl = "https://nominatim.openstreetmap.org/search";

    // Nominatim usage policy vyžaduje popisný User-Agent s kontaktem.
    private const string UserAgent = "ToiletLabels/1.0 (hausterlitz@gmail.com)";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public async Task<GeoPoint?> GeocodeAsync(string place, string city, string country, CancellationToken ct)
    {
        var parts = new[] { place, city, country }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToArray();
        if (parts.Length == 0)
        {
            return null;
        }

        var query = Uri.EscapeDataString(string.Join(", ", parts));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{SearchUrl}?q={query}&format=json&limit=1");
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            using var response = await http.SendAsync(request, timeout.Token);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return ParseFirst(body);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // zrušil volající, ne timeout
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static GeoPoint? ParseFirst(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
        {
            return null;
        }

        var first = doc.RootElement[0];
        var lat = ReadNumber(first, "lat");
        var lon = ReadNumber(first, "lon");
        return lat is { } la && lon is { } lo ? GeoPoint.Rounded(la, lo) : null;
    }

    private static double? ReadNumber(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        var parsed = value.ValueKind switch
        {
            JsonValueKind.String => double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : (double?)null,
            JsonValueKind.Number => value.GetDouble(),
            _ => null,
        };
        return parsed is { } n && double.IsFinite(n) ? n : null;
    }
}
