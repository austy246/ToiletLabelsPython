using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ToiletLabels.Geo;
using ToiletLabels.Images;
using ToiletLabels.Labels;

namespace ToiletLabels.Pages;

/// <summary>Společný základ stránek nahrávání a úpravy: pole formuláře, validace textů, čtení souborů.</summary>
public abstract class LabelFormPageModel(IImageStore images, IOptions<MapyOptions> mapy) : PageModel
{
    public const string MissingMenImage = "Nahraj fotku – muži.";
    public const string MissingWomenImage = "Nahraj fotku – ženy.";
    public const string NotAnImage = "Soubor není obrázek.";
    public const string TextTooLong = "Text je příliš dlouhé.";

    private const int MaxPlaceLength = 200;
    private const int MaxDescriptionLength = 2000;

    /// <summary>Chybové hlášky jsou konstanty; tento kodér je vypíše česky, ne jako entity.</summary>
    public static HtmlEncoder ErrorEncoder { get; } = HtmlEncoder.Create(
        UnicodeRanges.BasicLatin, UnicodeRanges.Latin1Supplement, UnicodeRanges.LatinExtendedA, UnicodeRanges.GeneralPunctuation);

    [BindProperty(Name = "place")]
    public string? Place { get; set; }

    [BindProperty(Name = "description")]
    public string? Description { get; set; }

    [BindProperty(Name = "country")]
    public string? Country { get; set; }

    [BindProperty(Name = "city")]
    public string? City { get; set; }

    [BindProperty(Name = "latitude")]
    public string? Latitude { get; set; }

    [BindProperty(Name = "longitude")]
    public string? Longitude { get; set; }

    [BindProperty(Name = "men_image")]
    public IFormFile? MenImage { get; set; }

    [BindProperty(Name = "women_image")]
    public IFormFile? WomenImage { get; set; }

    public List<string> Errors { get; } = [];

    public abstract bool IsEdit { get; }

    public abstract string Heading { get; }

    public abstract string SubmitText { get; }

    /// <summary>URL současného náhledu (jen úprava): náhled, jinak originál; jinak "".</summary>
    public virtual string MenPreviewUrl => "";

    public virtual string WomenPreviewUrl => "";

    /// <summary>Předvybraná země: kanonický název, nebo neznámá (legacy) hodnota, která se zachová.</summary>
    public string SelectedCountry => Countries.Normalize(Country);

    public IReadOnlyList<string> CountryOptions => Countries.All;

    public bool ShowPicker => !string.IsNullOrEmpty(mapy.Value.ApiKey);

    public LocationPickerModel Picker
    {
        get
        {
            var point = ManualCoordinates.Parse(Latitude, Longitude);
            return new LocationPickerModel(
                JsonSerializer.Serialize(new
                {
                    apiKey = mapy.Value.ApiKey,
                    lat = point?.Latitude,
                    lon = point?.Longitude,
                }),
                point is null ? "" : Format(point.Value.Latitude),
                point is null ? "" : Format(point.Value.Longitude));
        }
    }

    protected string PreviewUrl(string? thumb, string? original) =>
        images.PublicUrl(MapPoints.Preferred(thumb ?? "", original ?? ""));

    protected LabelInput ToInput() =>
        new(Place ?? "", Description ?? "", Country ?? "", City ?? "", Latitude, Longitude);

    /// <summary>Přidá chybu, když je některý text delší než povolený limit (Place, City, Country 200, Description 2000).</summary>
    protected void ValidateTexts()
    {
        if ((Place?.Length ?? 0) > MaxPlaceLength
            || (City?.Length ?? 0) > MaxPlaceLength
            || (Country?.Length ?? 0) > MaxPlaceLength
            || (Description?.Length ?? 0) > MaxDescriptionLength)
        {
            Errors.Add(TextTooLong);
        }
    }

    /// <summary>Soubor z formuláře; prázdná část (nic nevybráno) je <c>null</c>.</summary>
    protected static async Task<UploadedImage?> ReadAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return null;
        }

        var data = new byte[file.Length];
        await using var stream = file.OpenReadStream();
        await stream.ReadExactlyAsync(data, ct);
        return new UploadedImage(file.FileName, data);
    }

    private static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Model částečné stránky s mapou: konfigurace jako JSON (bezpečný uvnitř skriptu) a počáteční hodnoty skrytých polí.</summary>
public sealed record LocationPickerModel(string ConfigJson, string Latitude, string Longitude);
