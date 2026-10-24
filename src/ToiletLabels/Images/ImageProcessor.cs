using ImageMagick;
using ToiletLabels.Geo;

namespace ToiletLabels.Images;

/// <summary>Výsledek prohlídky nahraného souboru: Content-Type podle skutečného formátu a poloha z EXIF.</summary>
public sealed record ImageInfo(string ContentType, GeoPoint? Gps);

public interface IImageProcessor
{
    /// <summary>Vrátí <c>null</c>, když data nejsou čitelný obrázek.</summary>
    ImageInfo? Inspect(byte[] data);

    /// <summary>WebP náhled (auto-orient podle EXIF, max 400×400, bez zvětšování, kvalita 80); <c>null</c> při jakékoli chybě.</summary>
    byte[]? MakeThumbnail(byte[] data);
}

public sealed class ImageProcessor : IImageProcessor
{
    private const uint MaxThumbSide = 400;
    private const uint ThumbQuality = 80;

    static ImageProcessor()
    {
        // Strop paměti pro ImageMagick: velká fotka nesmí na malém VPS shodit proces.
        ResourceLimits.Memory = 200UL * 1024 * 1024;
    }

    public ImageInfo? Inspect(byte[] data)
    {
        try
        {
            // Ping načte jen hlavičku a metadata (EXIF), pixely se nedekódují.
            using var image = new MagickImage();
            image.Ping(data);
            return new ImageInfo(ContentTypeOf(image.Format), ReadGps(image));
        }
        catch (Exception)
        {
            return null;
        }
    }

    public byte[]? MakeThumbnail(byte[] data)
    {
        try
        {
            using var image = new MagickImage(data);
            // Orientaci z EXIF zapéct do pixelů, protože EXIF se do náhledu nekopíruje.
            image.AutoOrient();
            image.Resize(new MagickGeometry(MaxThumbSide, MaxThumbSide) { Greater = true });
            // GPS a další metadata do veřejného náhledu nepatří; ICC profil zůstává kvůli barvám.
            image.RemoveProfile("exif");
            image.RemoveProfile("xmp");
            image.Quality = ThumbQuality;
            image.Format = MagickFormat.WebP;
            return image.ToByteArray();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string ContentTypeOf(MagickFormat format) => format switch
    {
        MagickFormat.Jpeg or MagickFormat.Jpg or MagickFormat.Pjpeg => "image/jpeg",
        MagickFormat.Png or MagickFormat.Png8 or MagickFormat.Png24 or MagickFormat.Png32
            or MagickFormat.Png48 or MagickFormat.Png64 or MagickFormat.Png00 => "image/png",
        MagickFormat.Heic or MagickFormat.Heif => "image/heic",
        MagickFormat.WebP => "image/webp",
        MagickFormat.Gif or MagickFormat.Gif87 => "image/gif",
        _ => "application/octet-stream",
    };

    private static GeoPoint? ReadGps(IMagickImage<byte> image)
    {
        var exif = image.GetExifProfile();
        if (exif is null)
        {
            return null;
        }

        var lat = exif.GetValue(ExifTag.GPSLatitude)?.Value;
        var latRef = exif.GetValue(ExifTag.GPSLatitudeRef)?.Value;
        var lon = exif.GetValue(ExifTag.GPSLongitude)?.Value;
        var lonRef = exif.GetValue(ExifTag.GPSLongitudeRef)?.Value;
        if (lat is null || latRef is null || lon is null || lonRef is null)
        {
            return null;
        }

        var latDeg = DmsToDegrees(lat);
        var lonDeg = DmsToDegrees(lon);
        if (latDeg is null || lonDeg is null)
        {
            return null;
        }

        if (latRef.StartsWith("S", StringComparison.OrdinalIgnoreCase))
        {
            latDeg = -latDeg;
        }

        if (lonRef.StartsWith("W", StringComparison.OrdinalIgnoreCase))
        {
            lonDeg = -lonDeg;
        }

        return GeoPoint.Rounded(latDeg.Value, lonDeg.Value);
    }

    private static double? DmsToDegrees(Rational[] dms)
    {
        if (dms.Length != 3)
        {
            return null;
        }

        var parts = dms.Select(r => r.Denominator == 0 ? double.NaN : r.ToDouble()).ToArray();
        if (parts.Any(double.IsNaN))
        {
            return null;
        }

        return parts[0] + parts[1] / 60 + parts[2] / 3600;
    }
}

