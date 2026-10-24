using System.Globalization;

namespace ToiletLabels.Geo;

/// <summary>Ruční zadání souřadnic z formuláře (desetinná tečka, nezávisle na kultuře).</summary>
public static class ManualCoordinates
{
    /// <summary>
    /// Vrátí zaokrouhlený bod, jen když jsou obě hodnoty neprázdné, číselné, konečné
    /// a v rozsahu ±90 / ±180. Jinak <c>null</c>.
    /// </summary>
    public static GeoPoint? Parse(string? lat, string? lon)
    {
        if (string.IsNullOrWhiteSpace(lat) || string.IsNullOrWhiteSpace(lon))
        {
            return null;
        }

        if (!double.TryParse(lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var la)
            || !double.TryParse(lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lo))
        {
            return null;
        }

        if (!double.IsFinite(la) || !double.IsFinite(lo) || Math.Abs(la) > 90 || Math.Abs(lo) > 180)
        {
            return null;
        }

        return GeoPoint.Rounded(la, lo);
    }
}
