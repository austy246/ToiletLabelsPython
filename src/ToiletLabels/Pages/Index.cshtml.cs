using System.Text.Json;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ToiletLabels.Labels;
using ToiletLabels.Storage;

namespace ToiletLabels.Pages;

public class IndexModel(
    ILabelRepository labels,
    IOptions<StorageOptions> storage,
    IOptions<MapyOptions> mapy) : PageModel
{
    public IReadOnlyList<Label> Labels { get; private set; } = [];

    public string BlobBaseUrl => storage.Value.PublicBlobBaseUrl;

    /// <summary>JSON pro <c>&lt;script type="application/json"&gt;</c>; System.Text.Json escapuje &lt;, &gt; a &amp;.</summary>
    public string MapPointsJson { get; private set; } = "[]";

    /// <summary>API klíč jako JSON řetězec (včetně uvozovek), bezpečný uvnitř skriptu.</summary>
    public string MapyApiKeyJson => JsonSerializer.Serialize(mapy.Value.ApiKey);

    public bool ShowMap { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Labels = await labels.ListAsync(cancellationToken);
        var points = MapPoints.Build(Labels, BlobBaseUrl);
        MapPointsJson = JsonSerializer.Serialize(points);
        ShowMap = points.Count > 0 && !string.IsNullOrEmpty(mapy.Value.ApiKey);
    }
}
