using Microsoft.Extensions.Logging;
using ToiletLabels.Geo;
using ToiletLabels.Images;

namespace ToiletLabels.Labels;

/// <summary>Textová pole formuláře. Souřadnice jsou surové řetězce z mapy (viz <see cref="ManualCoordinates"/>).</summary>
public sealed record LabelInput(string Place, string Description, string Country, string City, string? Latitude, string? Longitude);

/// <summary>Nahraný soubor: původní jméno (kvůli příponě) a obsah.</summary>
public sealed record UploadedImage(string FileName, byte[] Data);

/// <summary>Nahraný soubor není obrázek ve formátu, který web zobrazí (jpeg, png, heic, webp, gif).</summary>
public sealed class InvalidImageException(string side) : Exception($"The {side} file is not a supported image.")
{
    /// <summary>"men" nebo "women".</summary>
    public string Side { get; } = side;
}

/// <summary>
/// Zápis štítku: obrázky do blobů, metadata do tabulky. Pořadí je volené tak, aby se při chybě
/// nezůstal záznam odkazující na chybějící obrázek: nejdřív bloby, pak tabulka, staré bloby se mažou až nakonec.
/// </summary>
public sealed class LabelEditor(
    ILabelRepository labels,
    IImageStore images,
    IImageProcessor processor,
    IGeocoder geocoder,
    TimeProvider time,
    ILogger<LabelEditor> logger)
{
    private const string Men = "men";
    private const string Women = "women";
    private const string UnknownContentType = "application/octet-stream";
    private const string ThumbContentType = "image/webp";

    /// <summary>Vytvoří štítek a vrátí jeho id. Neplatný obrázek: <see cref="InvalidImageException"/>, nic se nezapíše.</summary>
    public async Task<string> CreateAsync(LabelInput input, UploadedImage men, UploadedImage women, CancellationToken ct)
    {
        var menImage = Inspect(Men, men);
        var womenImage = Inspect(Women, women);

        var country = Countries.Normalize(input.Country);
        var point = ManualCoordinates.Parse(input.Latitude, input.Longitude)
            ?? await CoordinateResolver.ResolveForCreateAsync(
                menImage.Info.Gps, womenImage.Info.Gps, input.Place, input.City, country, geocoder, ct);

        var id = Guid.NewGuid().ToString();
        var written = new List<string>();
        try
        {
            var stored = await StoreAsync(id, [menImage, womenImage], written, ct);
            await labels.CreateAsync(new Label
            {
                Id = id,
                Place = input.Place,
                Description = input.Description,
                Country = country,
                City = input.City,
                MenImage = stored[Men].Original,
                WomenImage = stored[Women].Original,
                MenThumb = stored[Men].Thumb,
                WomenThumb = stored[Women].Thumb,
                Latitude = point?.Latitude,
                Longitude = point?.Longitude,
                Created = CreatedTimestamp.Now(time),
            }, ct);
        }
        catch
        {
            await RemoveAsync(written);
            throw;
        }

        return id;
    }

    /// <summary>
    /// Upraví štítek. <c>false</c> = štítek neexistuje. Nový obrázek nahradí starý; jiné jméno
    /// (např. jiná přípona) znamená, že se starý blob po úspěšném zápisu smaže.
    /// </summary>
    public async Task<bool> UpdateAsync(string id, LabelInput input, UploadedImage? men, UploadedImage? women, CancellationToken ct)
    {
        var existing = await labels.GetAsync(id, ct);
        if (existing is null)
        {
            return false;
        }

        var replacements = new List<Prepared>();
        if (men is not null)
        {
            replacements.Add(Inspect(Men, men));
        }

        if (women is not null)
        {
            replacements.Add(Inspect(Women, women));
        }

        var country = Countries.Normalize(input.Country);
        var point = ManualCoordinates.Parse(input.Latitude, input.Longitude)
            ?? await CoordinateResolver.ResolveForEditAsync(
                replacements.FirstOrDefault(r => r.Side == Men)?.Info.Gps,
                replacements.FirstOrDefault(r => r.Side == Women)?.Info.Gps,
                existing is { Latitude: { } lat, Longitude: { } lon } ? new GeoPoint(lat, lon) : null,
                input.Place, input.City, country, geocoder, ct);

        var written = new List<string>();
        try
        {
            var stored = await StoreAsync(id, replacements, written, ct);
            var updated = existing with
            {
                Place = input.Place,
                Description = input.Description,
                Country = country,
                City = input.City,
                Latitude = point?.Latitude,
                Longitude = point?.Longitude,
            };
            if (stored.TryGetValue(Men, out var newMen))
            {
                updated = updated with { MenImage = newMen.Original, MenThumb = newMen.Thumb };
            }

            if (stored.TryGetValue(Women, out var newWomen))
            {
                updated = updated with { WomenImage = newWomen.Original, WomenThumb = newWomen.Thumb };
            }

            await labels.UpdateAsync(updated, ct);

            // Co se přepsalo pod stejným jménem, už nejde vrátit zpět. Staré bloby s jiným jménem se mažou až teď.
            var obsolete = new[]
            {
                (existing.MenImage, updated.MenImage), (existing.MenThumb, updated.MenThumb),
                (existing.WomenImage, updated.WomenImage), (existing.WomenThumb, updated.WomenThumb),
            };
            await RemoveAsync(obsolete.Where(p => p.Item1.Length > 0 && p.Item1 != p.Item2).Select(p => p.Item1));
            return true;
        }
        catch
        {
            // Bloby, které přepsaly staré (stejné jméno), se nemažou: štítek na ně dál odkazuje.
            var referenced = new HashSet<string>([existing.MenImage, existing.WomenImage, existing.MenThumb, existing.WomenThumb]);
            await RemoveAsync(written.Where(name => !referenced.Contains(name)));
            throw;
        }
    }

    private Prepared Inspect(string side, UploadedImage upload)
    {
        var info = processor.Inspect(upload.Data);
        // Magick přečte i formáty, které prohlížeče nezobrazí (např. BMP, TIFF); ty se odmítají.
        if (info is null || info.ContentType == UnknownContentType)
        {
            throw new InvalidImageException(side);
        }

        return new Prepared(side, upload, info);
    }

    /// <summary>Nahraje originály a pak náhledy. Jména zapsaná do <paramref name="written"/> slouží k úklidu při chybě.</summary>
    private async Task<Dictionary<string, StoredImage>> StoreAsync(
        string id, IReadOnlyList<Prepared> prepared, List<string> written, CancellationToken ct)
    {
        var originals = new Dictionary<string, string>();
        foreach (var image in prepared)
        {
            var name = BlobNames.Original(id, image.Side, image.Upload.FileName);
            await images.UploadAsync(name, image.Upload.Data, image.Info.ContentType, ct);
            written.Add(name);
            originals[image.Side] = name;
        }

        var stored = new Dictionary<string, StoredImage>();
        foreach (var image in prepared)
        {
            var original = originals[image.Side];
            var thumb = "";
            var thumbData = processor.MakeThumbnail(image.Upload.Data);
            if (thumbData is not null)
            {
                thumb = BlobNames.Thumb(original);
                await images.UploadAsync(thumb, thumbData, ThumbContentType, ct);
                written.Add(thumb);
            }

            stored[image.Side] = new StoredImage(original, thumb);
        }

        return stored;
    }

    /// <summary>Úklid po chybě nebo po úspěšné výměně. Selhání se jen zaloguje, žádost už nemá smysl shazovat.</summary>
    private async Task RemoveAsync(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            try
            {
                await images.DeleteIfExistsAsync(name, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not delete blob {BlobName}.", name);
            }
        }
    }

    private sealed record Prepared(string Side, UploadedImage Upload, ImageInfo Info);

    private sealed record StoredImage(string Original, string Thumb);
}
