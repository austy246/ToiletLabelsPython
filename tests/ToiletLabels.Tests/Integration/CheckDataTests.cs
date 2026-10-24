using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using ToiletLabels.Cli;
using ToiletLabels.Labels;

namespace ToiletLabels.Tests.Integration;

public class CheckDataTests(AzuriteFixture azurite)
{
    [Fact]
    public async Task CheckData_ReportsCountsAndTypes()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new TestStorage(azurite);
        await storage.LabelsTableClient.CreateIfNotExistsAsync(ct);
        await storage.LabelsTableClient.AddEntityAsync(LegacyEntities.A(Guid.NewGuid().ToString()), ct);
        await storage.LabelsTableClient.AddEntityAsync(LegacyEntities.B(Guid.NewGuid().ToString()), ct);
        var output = new StringWriter();

        var exitCode = await CheckDataCommand.RunAsync(ConfigFor(storage), output, ct);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Labels: 2, unreadable: 0", text);
        Assert.Contains("Without coordinates: 1", text);
        Assert.Contains("Without thumbnails: 1", text);
        Assert.Contains("Without Created: 1", text);
        Assert.Contains("NumVoters types: Int32, Int64", text);
        Assert.Contains("AvgVote types: Double, Int32", text);
    }

    [Fact]
    public async Task CheckData_MissingTable_FailsWithoutCreating()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new TestStorage(azurite);
        var output = new StringWriter();

        var exitCode = await CheckDataCommand.RunAsync(ConfigFor(storage), output, ct);

        Assert.Equal(1, exitCode);
        Assert.Contains($"Table {storage.Options.LabelsTable} not found", output.ToString());
        Assert.False(await TableExists(storage, storage.Options.LabelsTable, ct));
    }

    [Fact]
    public async Task CheckData_DoesNotCreateUsersTableOrKeys()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new TestStorage(azurite);
        await storage.LabelsTableClient.CreateIfNotExistsAsync(ct);
        await storage.LabelsTableClient.AddEntityAsync(LegacyEntities.A(Guid.NewGuid().ToString()), ct);

        await CheckDataCommand.RunAsync(ConfigFor(storage), new StringWriter(), ct);

        Assert.False(await TableExists(storage, storage.Options.UsersTable, ct));
        Assert.False(await storage.Blobs.GetBlobContainerClient(storage.Options.KeysContainer).ExistsAsync(ct));
        Assert.False(await storage.Blobs.GetBlobContainerClient(storage.Options.ImagesContainer).ExistsAsync(ct));
    }

    [Fact]
    public async Task CheckData_ListsUnreadableEntitiesAndFails()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new TestStorage(azurite);
        await storage.LabelsTableClient.CreateIfNotExistsAsync(ct);
        var goodId = Guid.NewGuid().ToString();
        var badId = Guid.NewGuid().ToString();
        await storage.LabelsTableClient.AddEntityAsync(LegacyEntities.A(goodId), ct);
        await storage.LabelsTableClient.AddEntityAsync(LegacyEntities.B(badId), ct);
        var output = new StringWriter();

        // Skutečná data mapper přečte vždy, nečitelnou entitu proto simuluje vlastní převodník.
        var exitCode = await CheckDataCommand.RunAsync(ConfigFor(storage), output, e =>
            e.RowKey == badId ? throw new FormatException("bad value") : LabelEntityMapper.FromEntity(e), ct);

        var text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("Labels: 2, unreadable: 1", text);
        Assert.Contains($"{badId}: bad value", text);
    }

    [Fact]
    public async Task CheckData_MissingConnectionString_FailsWithMessage()
    {
        var output = new StringWriter();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();

        var exitCode = await CheckDataCommand.RunAsync(config, output, TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        Assert.Contains("ConnectionStrings:Storage", output.ToString());
    }

    private IConfiguration ConfigFor(TestStorage storage) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Storage"] = azurite.ConnectionString,
            ["Storage:LabelsTable"] = storage.Options.LabelsTable,
            ["Storage:UsersTable"] = storage.Options.UsersTable,
            ["Storage:ImagesContainer"] = storage.Options.ImagesContainer,
            ["Storage:KeysContainer"] = storage.Options.KeysContainer,
        })
        .Build();

    private static async Task<bool> TableExists(TestStorage storage, string name, CancellationToken ct)
    {
        await foreach (var _ in storage.Tables.QueryAsync(t => t.Name == name, cancellationToken: ct))
            return true;
        return false;
    }

    private static async Task<int> CountAsync(TestStorage storage, CancellationToken ct)
    {
        var count = 0;
        await foreach (var _ in storage.LabelsTableClient.QueryAsync<TableEntity>(cancellationToken: ct))
            count++;
        return count;
    }
}
