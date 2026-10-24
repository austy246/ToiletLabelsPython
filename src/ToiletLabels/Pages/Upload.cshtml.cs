using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ToiletLabels.Images;
using ToiletLabels.Labels;
using ToiletLabels.Users;

namespace ToiletLabels.Pages;

[Authorize(Policy = AuthSetup.AdminPolicy)]
public class UploadModel(LabelEditor editor, IImageStore images, IOptions<MapyOptions> mapy)
    : LabelFormPageModel(images, mapy)
{
    public override bool IsEdit => false;

    public override string Heading => "Upload Toilet Label";

    public override string SubmitText => "Upload";

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var men = await ReadAsync(MenImage, ct);
        var women = await ReadAsync(WomenImage, ct);
        if (men is null)
        {
            Errors.Add(MissingMenImage);
        }

        if (women is null)
        {
            Errors.Add(MissingWomenImage);
        }

        ValidateTexts();
        if (Errors.Count > 0)
        {
            return Page();
        }

        try
        {
            await editor.CreateAsync(ToInput(), men!, women!, ct);
        }
        catch (InvalidImageException)
        {
            Errors.Add(NotAnImage);
            return Page();
        }

        return LocalRedirect("/");
    }
}
