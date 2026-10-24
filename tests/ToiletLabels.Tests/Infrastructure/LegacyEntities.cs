using Azure.Data.Tables;

namespace ToiletLabels.Tests;

/// <summary>
/// Dva tvary entit, které v produkční tabulce skutečně existují (zapsal je starý Python kód).
/// Používají se k ověření, že nová aplikace čte i mění data bez migrace.
/// </summary>
public static class LegacyEntities
{
    /// <summary>
    /// Starý tvar A: prázdné řetězce místo souřadnic, počitadla jako Int32,
    /// bez náhledů, země a města.
    /// </summary>
    public static TableEntity A(string rowKey) => new("label", rowKey)
    {
        ["Place"] = "Legacy A",
        ["Description"] = "Starý záznam",
        ["MenImageUrl"] = rowKey + "_men.jpg",
        ["WomenImageUrl"] = rowKey + "_women.jpg",
        ["Latitude"] = "",
        ["Longitude"] = "",
        ["NumVoters"] = 0,
        ["AvgVote"] = 0,
        ["Created"] = "2026-01-02T10:00:00.000000",
    };

    /// <summary>
    /// Starý tvar B: číselné souřadnice, NumVoters jako Int64, AvgVote jako Double,
    /// bez vlastnosti Created.
    /// </summary>
    public static TableEntity B(string rowKey) => new("label", rowKey)
    {
        ["Place"] = "Legacy B",
        ["Description"] = "",
        ["MenImageUrl"] = rowKey + "_men.png",
        ["WomenImageUrl"] = rowKey + "_women.png",
        ["MenThumbUrl"] = rowKey + "_men_thumb.webp",
        ["WomenThumbUrl"] = rowKey + "_women_thumb.webp",
        ["Country"] = "Česko",
        ["City"] = "Praha",
        ["Latitude"] = 50.08,
        ["Longitude"] = 14.43,
        ["NumVoters"] = 2L,
        ["AvgVote"] = 4.5,
    };
}
