namespace ToiletLabels.Images;

/// <summary>Úložiště obrázků štítků (Azure Blob Storage). Jména odpovídají hodnotám v tabulce.</summary>
public interface IImageStore
{
    /// <summary>Nahraje blob s daným Content-Type; existující blob stejného jména přepíše.</summary>
    Task UploadAsync(string name, byte[] data, string contentType, CancellationToken ct);

    Task DeleteIfExistsAsync(string name, CancellationToken ct);

    /// <summary>Veřejná URL blobu, nebo "" pro prázdné jméno.</summary>
    string PublicUrl(string name);
}
