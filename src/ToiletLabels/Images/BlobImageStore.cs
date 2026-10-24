using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using ToiletLabels.Storage;

namespace ToiletLabels.Images;

public sealed class BlobImageStore(BlobServiceClient blobs, IOptions<StorageOptions> options) : IImageStore
{
    private readonly BlobContainerClient _container = blobs.GetBlobContainerClient(options.Value.ImagesContainer);
    private readonly string _publicBaseUrl = options.Value.PublicBlobBaseUrl;

    public async Task UploadAsync(string name, byte[] data, string contentType, CancellationToken ct)
    {
        // Bez podmínek v BlobUploadOptions se existující blob přepíše.
        var uploadOptions = new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } };
        await _container.GetBlobClient(name).UploadAsync(BinaryData.FromBytes(data), uploadOptions, ct);
    }

    public async Task DeleteIfExistsAsync(string name, CancellationToken ct) =>
        await _container.GetBlobClient(name).DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: ct);

    public string PublicUrl(string name) => string.IsNullOrEmpty(name) ? "" : _publicBaseUrl + name;
}
