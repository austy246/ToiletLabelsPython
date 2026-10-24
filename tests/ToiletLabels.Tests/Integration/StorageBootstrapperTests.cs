using Azure.Storage.Blobs.Models;

namespace ToiletLabels.Tests.Integration;

public class StorageBootstrapperTests(AzuriteFixture azurite)
{
    [Fact]
    public async Task Ensure_CreatesAllResources_Idempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new TestStorage(azurite);
        var bootstrapper = storage.Bootstrapper;

        await bootstrapper.EnsureAsync(isDevelopment: false, ct);
        await bootstrapper.EnsureAsync(isDevelopment: false, ct);

        await AssertTableExists(storage, storage.Options.LabelsTable, ct);
        await AssertTableExists(storage, storage.Options.UsersTable, ct);
        Assert.True(await storage.Blobs.GetBlobContainerClient(storage.Options.ImagesContainer).ExistsAsync(ct));
        Assert.True(await storage.Blobs.GetBlobContainerClient(storage.Options.KeysContainer).ExistsAsync(ct));
    }

    [Fact]
    public async Task Ensure_Production_KeepsContainersPrivate()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new TestStorage(azurite);

        await storage.Bootstrapper.EnsureAsync(isDevelopment: false, ct);

        Assert.Equal(PublicAccessType.None, await AccessOf(storage, storage.Options.ImagesContainer, ct));
        Assert.Equal(PublicAccessType.None, await AccessOf(storage, storage.Options.KeysContainer, ct));
    }

    [Fact]
    public async Task Ensure_Development_MakesNewImagesContainerPublic()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new TestStorage(azurite);

        await storage.Bootstrapper.EnsureAsync(isDevelopment: true, ct);

        Assert.Equal(PublicAccessType.Blob, await AccessOf(storage, storage.Options.ImagesContainer, ct));
        Assert.Equal(PublicAccessType.None, await AccessOf(storage, storage.Options.KeysContainer, ct));
    }

    [Fact]
    public async Task Ensure_DoesNotChangeExistingContainerAccess()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new TestStorage(azurite);
        await storage.Blobs.GetBlobContainerClient(storage.Options.ImagesContainer)
            .CreateAsync(PublicAccessType.None, cancellationToken: ct);

        await storage.Bootstrapper.EnsureAsync(isDevelopment: true, ct);

        Assert.Equal(PublicAccessType.None, await AccessOf(storage, storage.Options.ImagesContainer, ct));
    }

    [Fact]
    public async Task App_Startup_RunsBootstrapperBeforeServing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new AppFactory(azurite);

        using var client = factory.CreateClient();
        await client.GetAsync("/healthz", ct);

        var storage = new TestStorage(azurite);
        await AssertTableExists(storage, factory.LabelsTable, ct);
        await AssertTableExists(storage, factory.UsersTable, ct);
        Assert.True(await storage.Blobs.GetBlobContainerClient(factory.ImagesContainer).ExistsAsync(ct));
        Assert.True(await storage.Blobs.GetBlobContainerClient(factory.KeysContainer).ExistsAsync(ct));
        Assert.Equal(PublicAccessType.Blob, await AccessOf(storage, factory.ImagesContainer, ct));
    }

    private static async Task AssertTableExists(TestStorage storage, string name, CancellationToken ct)
    {
        var names = new List<string>();
        await foreach (var t in storage.Tables.QueryAsync(t => t.Name == name, cancellationToken: ct))
            names.Add(t.Name);
        Assert.Equal([name], names);
    }

    private static async Task<PublicAccessType> AccessOf(TestStorage storage, string container, CancellationToken ct) =>
        (await storage.Blobs.GetBlobContainerClient(container).GetAccessPolicyAsync(cancellationToken: ct)).Value.BlobPublicAccess;
}
