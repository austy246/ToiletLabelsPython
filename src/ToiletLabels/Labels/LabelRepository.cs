using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using ToiletLabels.Storage;

namespace ToiletLabels.Labels;

public interface ILabelRepository
{
    /// <summary>Všechny štítky, nejnovější první (<c>Created</c> sestupně, ordinal); bez <c>Created</c> na konci.</summary>
    Task<IReadOnlyList<Label>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Štítek podle id, nebo <c>null</c> (neexistuje i neplatný klíč).</summary>
    Task<Label?> GetAsync(string id, CancellationToken cancellationToken);

    Task CreateAsync(Label label, CancellationToken cancellationToken);

    /// <summary>Přepíše doménová pole; počitadla, <c>Created</c> a neznámé vlastnosti zůstanou beze změny.</summary>
    Task UpdateAsync(Label label, CancellationToken cancellationToken);

    /// <summary>Surové entity (pro kontrolu dat a migrační nástroje).</summary>
    IAsyncEnumerable<TableEntity> ListRawAsync(CancellationToken cancellationToken);
}

public sealed class LabelRepository(TableServiceClient tables, IOptions<StorageOptions> options) : ILabelRepository
{
    private const string PartitionFilter = "PartitionKey eq 'label'";

    private readonly TableClient _table = tables.GetTableClient(options.Value.LabelsTable);

    public async Task<IReadOnlyList<Label>> ListAsync(CancellationToken cancellationToken)
    {
        var labels = new List<Label>();
        await foreach (var entity in ListRawAsync(cancellationToken))
            labels.Add(LabelEntityMapper.FromEntity(entity));
        return labels.OrderByDescending(l => l.Created, StringComparer.Ordinal).ToList();
    }

    public async Task<Label?> GetAsync(string id, CancellationToken cancellationToken)
    {
        var entity = await GetEntityAsync(id, cancellationToken);
        return entity is null ? null : LabelEntityMapper.FromEntity(entity);
    }

    public Task CreateAsync(Label label, CancellationToken cancellationToken) =>
        _table.AddEntityAsync(LabelEntityMapper.ToEntity(label, null), cancellationToken);

    public async Task UpdateAsync(Label label, CancellationToken cancellationToken)
    {
        var existing = await GetEntityAsync(label.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Label '{label.Id}' does not exist.");
        // ETag načtené entity chrání před přepsáním souběžné změny (např. hlasování).
        await _table.UpdateEntityAsync(
            LabelEntityMapper.ToEntity(label, existing), existing.ETag, TableUpdateMode.Replace, cancellationToken);
    }

    public IAsyncEnumerable<TableEntity> ListRawAsync(CancellationToken cancellationToken) =>
        _table.QueryAsync<TableEntity>(PartitionFilter, cancellationToken: cancellationToken);

    private async Task<TableEntity?> GetEntityAsync(string id, CancellationToken cancellationToken)
    {
        if (!IsValidKey(id))
            return null;
        try
        {
            var result = await _table.GetEntityIfExistsAsync<TableEntity>(
                LabelEntityMapper.PartitionKey, id, cancellationToken: cancellationToken);
            return result.HasValue ? result.Value : null;
        }
        catch (RequestFailedException ex) when (ex.Status is 400 or 404)
        {
            // Neplatný klíč (např. zakázané znaky) je pro volajícího totéž co neexistující záznam.
            return null;
        }
    }

    // Klíč entity nesmí být prázdný ani obsahovat / \ # ? a řídicí znaky. Azurite na takové klíče
    // odpovídá 500, proto je odmítáme dřív, než se dotaz vůbec odešle.
    private static bool IsValidKey(string id) =>
        !string.IsNullOrEmpty(id)
        && id.All(c => c is not ('/' or '\\' or '#' or '?') && !char.IsControl(c));
}
