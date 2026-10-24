using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using ToiletLabels.Labels;
using ToiletLabels.Storage;

namespace ToiletLabels.Tests;

/// <summary>Pomocník pro integrační testy: unikátní jména tabulek a kontejnerů nad sdíleným Azurite.</summary>
public sealed class TestStorage
{
    public TableServiceClient Tables { get; }
    public BlobServiceClient Blobs { get; }
    public StorageOptions Options { get; }

    public TestStorage(AzuriteFixture azurite)
    {
        Tables = new TableServiceClient(azurite.ConnectionString);
        Blobs = new BlobServiceClient(azurite.ConnectionString);
        Options = new StorageOptions
        {
            LabelsTable = "Labels" + Hex(),
            UsersTable = "Users" + Hex(),
            ImagesContainer = "img-" + Hex(),
            KeysContainer = "keys-" + Hex(),
        };
    }

    public TableClient LabelsTableClient => Tables.GetTableClient(Options.LabelsTable);

    public TableClient UsersTableClient => Tables.GetTableClient(Options.UsersTable);

    public StorageBootstrapper Bootstrapper => new(Tables, Blobs, Microsoft.Extensions.Options.Options.Create(Options));

    /// <summary>Vytvoří tabulku štítků a vrátí repozitář nad ní.</summary>
    public async Task<LabelRepository> CreateRepositoryAsync(CancellationToken ct)
    {
        await LabelsTableClient.CreateIfNotExistsAsync(ct);
        return new LabelRepository(Tables, Microsoft.Extensions.Options.Options.Create(Options));
    }

    private static string Hex() => Guid.NewGuid().ToString("N")[..8];
}
