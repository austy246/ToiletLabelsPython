namespace ToiletLabels.Geo;

/// <summary>Zeměpisná poloha ve stupních (WGS 84).</summary>
public readonly record struct GeoPoint(double Latitude, double Longitude)
{
    /// <summary>Zaokrouhlí obě souřadnice na 6 desetinných míst (shodně s Python verzí).</summary>
    public static GeoPoint Rounded(double lat, double lon) =>
        new(Round6(lat), Round6(lon));

    private static double Round6(double value) =>
        Math.Round(value, 6, MidpointRounding.AwayFromZero);
}
