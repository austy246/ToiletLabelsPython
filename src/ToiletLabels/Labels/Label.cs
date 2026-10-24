namespace ToiletLabels.Labels;

/// <summary>
/// Dvojice cedulek (muži/ženy) se souřadnicemi a hodnocením. Do tabulky se ukládají jen
/// názvy blobů, plné URL skládají šablony z <c>Storage:PublicBlobBaseUrl</c>.
/// </summary>
public sealed record Label
{
    public required string Id { get; init; }
    public string Place { get; init; } = "";
    public string Description { get; init; } = "";
    public string Country { get; init; } = "";
    public string City { get; init; } = "";
    public string MenImage { get; init; } = "";
    public string WomenImage { get; init; } = "";
    public string MenThumb { get; init; } = "";
    public string WomenThumb { get; init; } = "";
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public int NumVoters { get; init; }
    public double AvgVote { get; init; }

    /// <summary>Surová hodnota <c>Created</c> z tabulky (formát viz <see cref="CreatedTimestamp"/>), nebo "".</summary>
    public string Created { get; init; } = "";

    public string Title =>
        !string.IsNullOrWhiteSpace(Place) ? Place
        : !string.IsNullOrWhiteSpace(City) ? City
        : "Untitled";
}
