using System.Globalization;
using System.Text;

namespace ToiletLabels.Labels;

/// <summary>
/// Kanonický seznam zemí (české názvy) pro výběr v rozbalovací nabídce a normalizaci.
/// Port <c>gallery/services/countries.py</c>.
/// </summary>
public static class Countries
{
    /// <summary>Pořadí v nabídce: sousedé první, zbytek abecedně.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        "Česko",
        "Slovensko",
        "Rakousko",
        "Německo",
        "Polsko",
        "Belgie",
        "Francie",
        "Chorvatsko",
        "Itálie",
        "Maďarsko",
        "Nizozemsko",
        "Portugalsko",
        "Řecko",
        "Slovinsko",
        "Spojené království",
        "Španělsko",
        "Švédsko",
        "Švýcarsko",
    ];

    // ISO 3166-1 alpha-2 kód ke každému kanonickému názvu, slouží k sestavení URL vlajky.
    private static readonly Dictionary<string, string> Iso = new()
    {
        ["Česko"] = "cz",
        ["Slovensko"] = "sk",
        ["Rakousko"] = "at",
        ["Německo"] = "de",
        ["Polsko"] = "pl",
        ["Belgie"] = "be",
        ["Francie"] = "fr",
        ["Chorvatsko"] = "hr",
        ["Itálie"] = "it",
        ["Maďarsko"] = "hu",
        ["Nizozemsko"] = "nl",
        ["Portugalsko"] = "pt",
        ["Řecko"] = "gr",
        ["Slovinsko"] = "si",
        ["Spojené království"] = "gb",
        ["Španělsko"] = "es",
        ["Švédsko"] = "se",
        ["Švýcarsko"] = "ch",
    };

    // Historické / cizí zápisy -> kanonický český název. Klíče se porovnávají po Fold.
    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["czechia"] = "Česko",
        ["czech republic"] = "Česko",
        ["ceska republika"] = "Česko",
        ["cesko"] = "Česko",
        ["slovakia"] = "Slovensko",
        ["austria"] = "Rakousko",
        ["germany"] = "Německo",
        ["poland"] = "Polsko",
        ["belgium"] = "Belgie",
        ["france"] = "Francie",
        ["croatia"] = "Chorvatsko",
        ["italy"] = "Itálie",
        ["hungary"] = "Maďarsko",
        ["netherlands"] = "Nizozemsko",
        ["portugal"] = "Portugalsko",
        ["greece"] = "Řecko",
        ["slovenia"] = "Slovinsko",
        ["united kingdom"] = "Spojené království",
        ["great britain"] = "Spojené království",
        ["uk"] = "Spojené království",
        ["spain"] = "Španělsko",
        ["sweden"] = "Švédsko",
        ["switzerland"] = "Švýcarsko",
    };

    // Kanonické názvy indexované podle složené podoby (pro rychlé přiřazení sebe sama).
    private static readonly Dictionary<string, string> CanonicalByFold =
        All.ToDictionary(Fold, name => name);

    /// <summary>ISO alpha-2 kód (malými písmeny) pro hodnotu země, nebo "" pokud je neznámá.</summary>
    public static string IsoCode(string? value) =>
        Iso.GetValueOrDefault(Normalize(value), "");

    /// <summary>
    /// Vrátí kanonický český název pro libovolně zapsanou hodnotu. Prázdný vstup zůstane "".
    /// Neznámá hodnota se vrací oříznutá, jinak beze změny, aby se nic neztratilo.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var key = Fold(value);
        if (CanonicalByFold.TryGetValue(key, out var canonical))
            return canonical;
        if (Aliases.TryGetValue(key, out var alias))
            return alias;
        return value.Trim();
    }

    // Odstraní diakritiku a velikost písmen pro porovnání nezávislé na obojím.
    private static string Fold(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().ToLowerInvariant().Trim();
    }
}
