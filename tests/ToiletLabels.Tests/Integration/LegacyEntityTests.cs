using Azure.Data.Tables;

namespace ToiletLabels.Tests.Integration;

public class LegacyEntityTests(AzuriteFixture azurite)
{
    [Fact]
    public async Task List_ReadsAllLegacyShapes()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new TestStorage(azurite);
        var repo = await storage.CreateRepositoryAsync(ct);
        var idA = Guid.NewGuid().ToString();
        var idB = Guid.NewGuid().ToString();
        await storage.LabelsTableClient.AddEntityAsync(LegacyEntities.A(idA), ct);
        await storage.LabelsTableClient.AddEntityAsync(LegacyEntities.B(idB), ct);

        var list = await repo.ListAsync(ct);

        Assert.Equal(2, list.Count);
        Assert.Equal([idA, idB], list.Select(l => l.Id));
        var a = list[0];
        var b = list[1];
        Assert.Null(a.Latitude);
        Assert.Null(a.Longitude);
        Assert.Equal("", a.City);
        Assert.Equal("", a.MenThumb);
        Assert.Equal(0, a.NumVoters);
        Assert.Equal(50.08, b.Latitude);
        Assert.Equal(2, b.NumVoters);
        Assert.Equal(4.5, b.AvgVote);
        Assert.Equal("", b.Created);
        Assert.Equal("Praha", b.City);
    }

    [Fact]
    public async Task Update_LegacyEntity_KeepsCounterTypes()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new TestStorage(azurite);
        var repo = await storage.CreateRepositoryAsync(ct);
        var id = Guid.NewGuid().ToString();
        await storage.LabelsTableClient.AddEntityAsync(LegacyEntities.A(id), ct);
        var a = await repo.GetAsync(id, ct);

        await repo.UpdateAsync(a! with { Place = "x" }, ct);

        var raw = (await storage.LabelsTableClient.GetEntityAsync<TableEntity>("label", id, cancellationToken: ct)).Value;
        Assert.Equal("x", raw["Place"]);
        Assert.IsType<int>(raw["NumVoters"]);
        Assert.IsType<int>(raw["AvgVote"]);
    }

    [Fact]
    public async Task Update_LegacyEntityB_KeepsInt64VotersAndMissingCreated()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new TestStorage(azurite);
        var repo = await storage.CreateRepositoryAsync(ct);
        var id = Guid.NewGuid().ToString();
        await storage.LabelsTableClient.AddEntityAsync(LegacyEntities.B(id), ct);
        var b = await repo.GetAsync(id, ct);

        await repo.UpdateAsync(b! with { Description = "nový popis" }, ct);

        var raw = (await storage.LabelsTableClient.GetEntityAsync<TableEntity>("label", id, cancellationToken: ct)).Value;
        Assert.Equal("nový popis", raw["Description"]);
        Assert.IsType<long>(raw["NumVoters"]);
        Assert.Equal(2L, raw["NumVoters"]);
        Assert.Equal(4.5, raw["AvgVote"]);
        Assert.False(raw.ContainsKey("Created"));
    }
}
