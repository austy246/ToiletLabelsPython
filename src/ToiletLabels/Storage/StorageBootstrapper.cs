using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace ToiletLabels.Storage;

/// <summary>Při startu vytvoří chybějící tabulky a kontejnery. Existující zdroje nikdy nemění.</summary>
public sealed class StorageBootstrapper(
    TableServiceClient tables,
    BlobServiceClient blobs,
    IOptions<StorageOptions> options)
{
    private readonly StorageOptions _options = options.Value;

    public async Task EnsureAsync(bool isDevelopment, CancellationToken cancellationToken)
    {
        await tables.CreateTableIfNotExistsAsync(_options.LabelsTable, cancellationToken);
        await tables.CreateTableIfNotExistsAsync(_options.UsersTable, cancellationToken);

        // Veřejný přístup k obrázkům jen u nově vytvořeného kontejneru ve vývoji (Azurite);
        // CreateIfNotExists u existujícího kontejneru nic nemění.
        var images = blobs.GetBlobContainerClient(_options.ImagesContainer);
        var imagesAccess = isDevelopment ? PublicAccessType.Blob : PublicAccessType.None;
        await images.CreateIfNotExistsAsync(imagesAccess, cancellationToken: cancellationToken);

        var keys = blobs.GetBlobContainerClient(_options.KeysContainer);
        await keys.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
    }
}
