namespace ToiletLabels.Labels;

/// <summary>Sekce <c>Mapy</c> v konfiguraci. Bez klíče se mapa nevykreslí.</summary>
public sealed class MapyOptions
{
    public const string SectionName = "Mapy";

    public string ApiKey { get; set; } = "";
}
