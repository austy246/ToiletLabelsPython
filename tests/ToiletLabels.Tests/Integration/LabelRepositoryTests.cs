using Azure.Data.Tables;
using ToiletLabels.Labels;

namespace ToiletLabels.Tests.Integration;

public class LabelRepositoryTests(AzuriteFixture azurite)
{
    private static Label New(string created, string place = "P") =>
        new() { Id = Guid.NewGuid().ToString(), Place = place, Created = created };

    [Fact]
    public async Task List_SortsByCreatedDesc_MissingLast()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = await new TestStorage(azurite).CreateRepositoryAsync(ct);
        var jan = New("2026-01-02T10:00:00.000000", "jan");
        var mar = New("2026-03-01T10:00:00.000000", "mar");
        var none = New("", "none");
        foreach (var l in new[] { jan, none, mar })
            await repo.CreateAsync(l, ct);

        var list = await repo.ListAsync(ct);

        Assert.Equal(["mar", "jan", "none"], list.Select(l => l.Place));
    }

    [Fact]
    public async Task Get_Unknown_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = await new TestStorage(azurite).CreateRepositoryAsync(ct);

        Assert.Null(await repo.GetAsync("does-not-exist", ct));
        Assert.Null(await repo.GetAsync("a'b", ct));
        Assert.Null(await repo.GetAsync("a/b", ct));
        Assert.Null(await repo.GetAsync("", ct));
    }

    [Fact]
    public async Task CreateThenGet_RoundTrips()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = await new TestStorage(azurite).CreateRepositoryAsync(ct);
        var label = new Label
        {
            Id = Guid.NewGuid().ToString(),
            Place = "Kavárna",
            Description = "Popis",
            Country = "Česko",
            City = "Praha",
            MenImage = "a_men.jpg",
            WomenImage = "a_women.jpg",
            MenThumb = "a_men_thumb.webp",
            WomenThumb = "a_women_thumb.webp",
            Latitude = 50.08,
            Longitude = 14.43,
            Created = "2026-06-03T12:34:56.123456",
        };

        await repo.CreateAsync(label, ct);

        Assert.Equal(label, await repo.GetAsync(label.Id, ct));
    }

    [Fact]
    public async Task Update_PreservesCreatedAndVotes()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new TestStorage(azurite);
        var repo = await storage.CreateRepositoryAsync(ct);
        var id = Guid.NewGuid().ToString();
        await storage.LabelsTableClient.AddEntityAsync(
            new TableEntity("label", id) { ["Place"] = "Staré", ["NumVoters"] = 4, ["AvgVote"] = 3.5, ["Created"] = "2026-02-02T00:00:00.000000" },
            ct);
        var loaded = await repo.GetAsync(id, ct);

        await repo.UpdateAsync(loaded! with { Place = "Nové", NumVoters = 100, AvgVote = 1, Created = "2030-01-01T00:00:00.000000" }, ct);

        var after = await repo.GetAsync(id, ct);
        Assert.Equal("Nové", after!.Place);
        Assert.Equal(4, after.NumVoters);
        Assert.Equal(3.5, after.AvgVote);
        Assert.Equal("2026-02-02T00:00:00.000000", after.Created);
    }

    [Fact]
    public async Task Update_NullCoords_RemovesThemFromStoredEntity()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new TestStorage(azurite);
        var repo = await storage.CreateRepositoryAsync(ct);
        var label = new Label { Id = Guid.NewGuid().ToString(), Latitude = 50.0, Longitude = 14.0 };
        await repo.CreateAsync(label, ct);

        await repo.UpdateAsync(label with { Latitude = null, Longitude = null }, ct);

        var raw = (await storage.LabelsTableClient.GetEntityAsync<TableEntity>("label", label.Id, cancellationToken: ct)).Value;
        Assert.False(raw.ContainsKey("Latitude"));
        Assert.False(raw.ContainsKey("Longitude"));
    }

    [Fact]
    public async Task Update_Unknown_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = await new TestStorage(azurite).CreateRepositoryAsync(ct);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repo.UpdateAsync(new Label { Id = Guid.NewGuid().ToString() }, ct));
    }

    [Fact]
    public async Task ListRaw_ReturnsStoredEntities()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = await new TestStorage(azurite).CreateRepositoryAsync(ct);
        var a = New("2026-01-01T00:00:00.000000");
        var b = New("2026-01-02T00:00:00.000000");
        await repo.CreateAsync(a, ct);
        await repo.CreateAsync(b, ct);

        var keys = new List<string>();
        await foreach (var e in repo.ListRawAsync(ct))
            keys.Add(e.RowKey);

        Assert.Equal(new[] { a.Id, b.Id }.Order(), keys.Order());
    }
}
