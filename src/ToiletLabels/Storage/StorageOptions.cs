namespace ToiletLabels.Storage;

/// <summary>Sekce <c>Storage</c> v konfiguraci. Jména se přepisují jen kvůli izolaci testů.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    private string _publicBlobBaseUrl = "";

    public string LabelsTable { get; set; } = "ToiletLabels";
    public string UsersTable { get; set; } = "ToiletLabelsUsers";
    public string ImagesContainer { get; set; } = "toiletlabels";
    public string KeysContainer { get; set; } = "toiletlabels-keys";

    /// <summary>Veřejná základní URL blobů; vždy končí lomítkem (nebo je prázdná).</summary>
    public string PublicBlobBaseUrl
    {
        get => _publicBlobBaseUrl;
        set => _publicBlobBaseUrl = string.IsNullOrWhiteSpace(value) ? "" : value.Trim().TrimEnd('/') + "/";
    }
}
