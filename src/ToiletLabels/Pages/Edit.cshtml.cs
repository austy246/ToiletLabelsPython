using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ToiletLabels.Images;
using ToiletLabels.Labels;
using ToiletLabels.Users;

namespace ToiletLabels.Pages;

[Authorize(Policy = AuthSetup.AdminPolicy)]
public class EditModel(ILabelRepository labels, LabelEditor editor, IImageStore images, IOptions<MapyOptions> mapy)
    : LabelFormPageModel(images, mapy)
{
    private Label? _existing;

    public override bool IsEdit => true;

    public override string Heading => "Edit Toilet Label";

    public override string SubmitText => "Save Changes";

    public override string MenPreviewUrl => PreviewUrl(_existing?.MenThumb, _existing?.MenImage);

    public override string WomenPreviewUrl => PreviewUrl(_existing?.WomenThumb, _existing?.WomenImage);

    public async Task<IActionResult> OnGetAsync(string id, CancellationToken ct)
    {
        _existing = await labels.GetAsync(id, ct);
        if (_existing is null)
        {
            return NotFound();
        }

        Place = _existing.Place;
        Description = _existing.Description;
        Country = _existing.Country;
        City = _existing.City;
        Latitude = _existing.Latitude?.ToString(CultureInfo.InvariantCulture);
        Longitude = _existing.Longitude?.ToString(CultureInfo.InvariantCulture);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string id, CancellationToken ct)
    {
        _existing = await labels.GetAsync(id, ct);
        if (_existing is null)
        {
            return NotFound();
        }

        var men = await ReadAsync(MenImage, ct);
        var women = await ReadAsync(WomenImage, ct);
        ValidateTexts(_existing);
        if (Errors.Count > 0)
        {
            return Page();
        }

        try
        {
            if (!await editor.UpdateAsync(id, ToInput(), men, women, ct))
            {
                return NotFound();
            }
        }
        catch (InvalidImageException)
        {
            Errors.Add(NotAnImage);
            return Page();
        }

        return LocalRedirect("/");
    }
}
