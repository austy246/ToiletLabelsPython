using ImageMagick;
using ToiletLabels.Labels;

namespace ToiletLabels.Cli;

/// <summary>
/// Režim <c>--selftest</c>: ověří, že runtime image umí to, co aplikace potřebuje (nativní Magick.NET s HEIC, WebP zápis,
/// ICU pro porovnání názvů zemí). Nesahá do storage ani do sítě. Návratový kód 0 = všechny kontroly prošly.
/// </summary>
public static class SelfTestCommand
{
    public static int Run(TextWriter output)
    {
        var allPassed = true;
        allPassed &= Check(output, "Magick.NET reads HEIC", CheckHeicReadable);
        allPassed &= Check(output, "WebP encode of a 10x10 image", CheckWebPWrite);
        allPassed &= Check(output, "Countries.Normalize folds diacritics", CheckCountryFold);
        return allPassed ? 0 : 1;
    }

    private static bool Check(TextWriter output, string name, Func<string?> check)
    {
        try
        {
            var problem = check();
            output.WriteLine(problem is null ? $"OK {name}" : $"FAIL {name}: {problem}");
            return problem is null;
        }
        catch (Exception ex)
        {
            output.WriteLine($"FAIL {name}: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static string? CheckHeicReadable()
    {
        var heic = MagickNET.SupportedFormats.FirstOrDefault(f => f.Format == MagickFormat.Heic);
        if (heic is null) return "HEIC is not in MagickNET.SupportedFormats";
        return heic.SupportsReading ? null : "HEIC is listed but cannot be read";
    }

    private static string? CheckWebPWrite()
    {
        using var image = new MagickImage(MagickColors.Red, 10, 10) { Format = MagickFormat.WebP, Quality = 80 };
        var bytes = image.ToByteArray();
        return bytes.Length > 0 ? null : "encoder produced no data";
    }

    private static string? CheckCountryFold()
    {
        var actual = Countries.Normalize("ČESKÁ republika");
        return actual == "Česko" ? null : $"expected 'Česko', got '{actual}'";
    }
}
