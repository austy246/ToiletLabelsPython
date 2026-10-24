using System.Text.RegularExpressions;

namespace ToiletLabels.Images;

/// <summary>Jména blobů s obrázky štítku. Zachovává konvenci původní Python aplikace.</summary>
public static partial class BlobNames
{
    /// <summary><c>{id}_{side}{přípona}</c>; <paramref name="side"/> je "men" nebo "women".</summary>
    public static string Original(string id, string side, string fileName) =>
        $"{id}_{side}{SafeExtension(fileName)}";

    /// <summary><c>{název bez přípony}_thumb.webp</c>.</summary>
    public static string Thumb(string originalName) =>
        $"{originalName[..^Path.GetExtension(originalName).Length]}_thumb.webp";

    // Přípona z nahraného jména (včetně tečky, velikost písmen zůstává). Dostane se do veřejné URL,
    // takže cokoli jiného než krátká ASCII alfanumerická přípona se zahodí. Typ obsahu se určuje z obsahu.
    private static string SafeExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return ExtensionPattern().IsMatch(extension) ? extension : "";
    }

    [GeneratedRegex(@"^\.[A-Za-z0-9]{1,10}\z")]
    private static partial Regex ExtensionPattern();
}
