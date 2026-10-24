using ImageMagick;
using ImageMagick.Formats;

namespace ToiletLabels.Tests.Infrastructure;

/// <summary>Generuje malé testovací obrázky v paměti (bez souborů a bez sítě).</summary>
public static class TestImages
{
    public static byte[] Jpeg(int w, int h, ushort? orientation = null, (double lat, double lon)? gps = null)
    {
        using var image = new MagickImage(MagickColors.SteelBlue, (uint)w, (uint)h);
        image.Format = MagickFormat.Jpeg;

        if (orientation is not null || gps is not null)
        {
            var exif = new ExifProfile();
            if (orientation is { } o)
            {
                exif.SetValue(ExifTag.Orientation, o);
                image.Orientation = (Orientation)o;
            }

            if (gps is { } g)
            {
                exif.SetValue(ExifTag.GPSLatitudeRef, g.lat < 0 ? "S" : "N");
                exif.SetValue(ExifTag.GPSLatitude, ToDms(Math.Abs(g.lat)));
                exif.SetValue(ExifTag.GPSLongitudeRef, g.lon < 0 ? "W" : "E");
                exif.SetValue(ExifTag.GPSLongitude, ToDms(Math.Abs(g.lon)));
            }

            image.SetProfile(exif);
        }

        return image.ToByteArray();
    }

    public static byte[] PngRgba(int w, int h)
    {
        using var image = new MagickImage(new MagickColor(0, 0, 255, 128), (uint)w, (uint)h);
        image.Format = MagickFormat.Png32;
        return image.ToByteArray();
    }

    // Vteřiny s přesností na 1e-6 vteřiny, aby zaokrouhlení na 6 míst stupně nebylo ovlivněno převodem.
    private static Rational[] ToDms(double degrees)
    {
        var d = Math.Floor(degrees);
        var minutesTotal = (degrees - d) * 60;
        var m = Math.Floor(minutesTotal);
        var s = (minutesTotal - m) * 60;
        return
        [
            new Rational((uint)d, 1),
            new Rational((uint)m, 1),
            new Rational((uint)Math.Round(s * 1_000_000), 1_000_000),
        ];
    }
}



