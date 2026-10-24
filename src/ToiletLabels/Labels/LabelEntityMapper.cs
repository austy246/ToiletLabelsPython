using System.Globalization;
using Azure.Data.Tables;

namespace ToiletLabels.Labels;

/// <summary>
/// Převod mezi <see cref="Label"/> a entitou v Azure Table Storage. Čtení je tolerantní vůči
/// všem tvarům, které produkční data skutečně obsahují (typy čísel, prázdné řetězce místo souřadnic,
/// chybějící vlastnosti). Zápis nemění typy ani hodnoty počitadel a <c>Created</c> existující entity.
/// </summary>
public static class LabelEntityMapper
{
    public const string PartitionKey = "label";

    public static Label FromEntity(TableEntity e) => new()
    {
        Id = e.RowKey,
        Place = Str(e, "Place"),
        Description = Str(e, "Description"),
        Country = Str(e, "Country"),
        City = Str(e, "City"),
        MenImage = Str(e, "MenImageUrl"),
        WomenImage = Str(e, "WomenImageUrl"),
        MenThumb = Str(e, "MenThumbUrl"),
        WomenThumb = Str(e, "WomenThumbUrl"),
        Latitude = Coordinate(e, "Latitude"),
        Longitude = Coordinate(e, "Longitude"),
        NumVoters = Voters(e, "NumVoters"),
        AvgVote = Number(e, "AvgVote") ?? 0.0,
        Created = Str(e, "Created"),
    };

    public static TableEntity ToEntity(Label label, TableEntity? existing)
    {
        var entity = existing is null ? new TableEntity() : new TableEntity(existing);
        entity.PartitionKey = PartitionKey;
        entity.RowKey = label.Id;

        entity["Place"] = label.Place;
        entity["Description"] = label.Description;
        entity["Country"] = label.Country;
        entity["City"] = label.City;
        entity["MenImageUrl"] = label.MenImage;
        entity["WomenImageUrl"] = label.WomenImage;
        entity["MenThumbUrl"] = label.MenThumb;
        entity["WomenThumbUrl"] = label.WomenThumb;
        SetCoordinate(entity, "Latitude", label.Latitude);
        SetCoordinate(entity, "Longitude", label.Longitude);

        if (existing is null)
        {
            entity["NumVoters"] = label.NumVoters;
            entity["AvgVote"] = label.AvgVote;
            entity["Created"] = label.Created;
        }

        return entity;
    }

    private static void SetCoordinate(TableEntity entity, string key, double? value)
    {
        if (value is { } v)
            entity[key] = v;
        else
            entity.Remove(key);
    }

    private static string Str(TableEntity e, string key) => e.TryGetValue(key, out var v)
        ? v switch
        {
            null => "",
            string s => s,
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => v.ToString() ?? "",
        }
        : "";

    private static double? Coordinate(TableEntity e, string key) =>
        Number(e, key) is { } n && double.IsFinite(n) ? n : null;

    private static int Voters(TableEntity e, string key)
    {
        var n = Number(e, key);
        if (n is null || double.IsNaN(n.Value))
            return 0;
        return (int)Math.Clamp(Math.Round(n.Value), int.MinValue, int.MaxValue);
    }

    // Číslo z Int32, Int64, Double i číselného řetězce; cokoli jiného (včetně "") je null.
    private static double? Number(TableEntity e, string key)
    {
        if (!e.TryGetValue(key, out var v))
            return null;
        return v switch
        {
            int i => i,
            long l => l,
            double d => d,
            float f => f,
            decimal m => (double)m,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }
}
