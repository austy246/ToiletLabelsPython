using Azure;
using Azure.Data.Tables;
using ToiletLabels.Labels;
using ToiletLabels.Storage;

namespace ToiletLabels.Cli;

/// <summary>
/// Režim <c>--check-data</c>: jen čte tabulku štítků a hlásí, co v ní je (souřadnice, náhledy, <c>Created</c>,
/// typy počitadel, nečitelné entity). Nic nezapisuje ani nezakládá, takže je bezpečný i proti produkčnímu storage.
/// Návratový kód: 0 = vše čitelné, 1 = nečitelná entita, chybějící tabulka nebo konfigurace.
/// </summary>
public static class CheckDataCommand
{
    private const string PartitionFilter = "PartitionKey eq 'label'";

    public static Task<int> RunAsync(IConfiguration config, TextWriter output, CancellationToken cancellationToken) =>
        RunAsync(config, output, LabelEntityMapper.FromEntity, cancellationToken);

    /// <summary>Varianta s vlastním převodníkem entity (kvůli testu větve s nečitelnými entitami).</summary>
    public static async Task<int> RunAsync(
        IConfiguration config, TextWriter output, Func<TableEntity, Label> read, CancellationToken cancellationToken)
    {
        var connectionString = config.GetConnectionString("Storage");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            await output.WriteLineAsync("Missing configuration value ConnectionStrings:Storage.");
            return 1;
        }

        var options = config.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
        var tables = new TableServiceClient(connectionString);
        var table = tables.GetTableClient(options.LabelsTable);

        var total = 0;
        var withoutCoordinates = 0;
        var withoutThumbs = 0;
        var withoutCreated = 0;
        var voterTypes = new SortedSet<string>(StringComparer.Ordinal);
        var voteTypes = new SortedSet<string>(StringComparer.Ordinal);
        var unreadable = new List<string>();
        int maxPlace = 0, maxDescription = 0, maxCity = 0, maxCountry = 0;

        try
        {
            await foreach (var entity in table.QueryAsync<TableEntity>(PartitionFilter, cancellationToken: cancellationToken))
            {
                total++;
                voterTypes.Add(TypeOf(entity, "NumVoters"));
                voteTypes.Add(TypeOf(entity, "AvgVote"));
                try
                {
                    var label = read(entity);
                    maxPlace = Math.Max(maxPlace, label.Place.Length);
                    maxDescription = Math.Max(maxDescription, label.Description.Length);
                    maxCity = Math.Max(maxCity, label.City.Length);
                    maxCountry = Math.Max(maxCountry, label.Country.Length);
                    if (label.Latitude is null || label.Longitude is null)
                        withoutCoordinates++;
                    if (label.MenThumb == "" || label.WomenThumb == "")
                        withoutThumbs++;
                    if (label.Created == "")
                        withoutCreated++;
                }
                catch (Exception ex)
                {
                    unreadable.Add($"{entity.RowKey}: {ex.Message}");
                }
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404 && ex.ErrorCode == "TableNotFound")
        {
            await output.WriteLineAsync($"Table {options.LabelsTable} not found");
            return 1;
        }

        await output.WriteLineAsync($"Storage account: {tables.AccountName}");
        await output.WriteLineAsync($"Labels: {total}, unreadable: {unreadable.Count}");
        await output.WriteLineAsync($"Max lengths: Place {maxPlace}, Description {maxDescription}, City {maxCity}, Country {maxCountry}");
        await output.WriteLineAsync($"Without coordinates: {withoutCoordinates}");
        await output.WriteLineAsync($"Without thumbnails: {withoutThumbs}");
        await output.WriteLineAsync($"Without Created: {withoutCreated}");
        await output.WriteLineAsync($"NumVoters types: {string.Join(", ", voterTypes)}");
        await output.WriteLineAsync($"AvgVote types: {string.Join(", ", voteTypes)}");
        foreach (var line in unreadable)
            await output.WriteLineAsync(line);

        return unreadable.Count == 0 ? 0 : 1;
    }

    private static string TypeOf(TableEntity entity, string key) =>
        entity.TryGetValue(key, out var value) ? value?.GetType().Name ?? "null" : "missing";
}
