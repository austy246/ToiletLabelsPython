using Azure.Data.Tables;
using Microsoft.Extensions.Logging.Abstractions;
using ToiletLabels.Geo;
using ToiletLabels.Images;
using ToiletLabels.Labels;

namespace ToiletLabels.Tests.Unit;

public class LabelEditorTests
{
    private static readonly byte[] Photo = [1, 2, 3];
    private static readonly byte[] Garbage = [];

    private readonly List<string> _log = [];
    private readonly FakeStore _store;
    private readonly FakeRepository _repo;
    private readonly FakeProcessor _processor = new();
    private readonly FakeGeocoder _geocoder = new();
    private readonly LabelEditor _sut;

    public LabelEditorTests()
    {
        _store = new FakeStore(_log);
        _repo = new FakeRepository(_log);
        _sut = new LabelEditor(_repo, _store, _processor, _geocoder, TimeProvider.System, NullLogger<LabelEditor>.Instance);
    }

    private static LabelInput Input(string? lat = null, string? lon = null) =>
        new("Cafe", "desc", "czech republic", "Praha", lat, lon);

    private static UploadedImage Image(string name = "a.jpg", byte[]? data = null) => new(name, data ?? Photo);

    private static Label Existing(string menThumb = "id1_men_thumb.webp") => new()
    {
        Id = "id1",
        Place = "Old",
        MenImage = "id1_men.jpg",
        WomenImage = "id1_women.jpg",
        MenThumb = menThumb,
        WomenThumb = "id1_women_thumb.webp",
        Latitude = 50,
        Longitude = 14,
        NumVoters = 2,
        AvgVote = 3,
        Created = "2026-01-01T00:00:00.000000",
    };

    private void Seed(Label label)
    {
        _repo.Labels[label.Id] = label;
        foreach (var name in new[] { label.MenImage, label.WomenImage, label.MenThumb, label.WomenThumb })
        {
            if (name.Length > 0)
                _store.Blobs[name] = "old";
        }

        _log.Clear();
    }

    [Fact]
    public async Task Create_StoresImagesThumbsAndLabel()
    {
        var id = await _sut.CreateAsync(Input("49.1", "16.6"), Image("m.JPG"), Image("w.png"), TestContext.Current.CancellationToken);

        Assert.Equal(
            new[] { $"{id}_men.JPG", $"{id}_men_thumb.webp", $"{id}_women.png", $"{id}_women_thumb.webp" }.Order(StringComparer.Ordinal),
            _store.Blobs.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("image/webp", _store.Blobs[$"{id}_men_thumb.webp"]);
        var label = Assert.Single(_repo.Labels.Values);
        Assert.Equal(id, label.Id);
        Assert.Equal("Česko", label.Country);
        Assert.Equal($"{id}_men.JPG", label.MenImage);
        Assert.Equal($"{id}_women_thumb.webp", label.WomenThumb);
        Assert.Equal((49.1, 16.6), (label.Latitude, label.Longitude));
        Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{6}$", label.Created);
        Assert.Equal(0, _geocoder.Calls);
    }

    [Fact]
    public async Task Create_WritesTableOnlyAfterAllBlobs()
    {
        await _sut.CreateAsync(Input(), Image(), Image(), TestContext.Current.CancellationToken);

        Assert.Equal("repo:create", _log[^1]);
        Assert.Equal(4, _log.Count(l => l.StartsWith("upload:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Create_UnreadableImage_ThrowsAndWritesNothing()
    {
        var ex = await Assert.ThrowsAsync<InvalidImageException>(() =>
            _sut.CreateAsync(Input(), Image(), Image(data: Garbage), TestContext.Current.CancellationToken));

        Assert.Equal("women", ex.Side);
        Assert.Empty(_log);
    }

    [Fact]
    public async Task Create_UnsupportedFormat_IsRejected()
    {
        _processor.ContentType = "application/octet-stream";

        await Assert.ThrowsAsync<InvalidImageException>(() =>
            _sut.CreateAsync(Input(), Image(), Image(), TestContext.Current.CancellationToken));

        Assert.Empty(_log);
    }

    [Fact]
    public async Task Create_ThumbnailFailure_LeavesThumbNameEmpty()
    {
        _processor.ThumbFails = true;

        await _sut.CreateAsync(Input(), Image(), Image(), TestContext.Current.CancellationToken);

        var label = Assert.Single(_repo.Labels.Values);
        Assert.Equal("", label.MenThumb);
        Assert.Equal("", label.WomenThumb);
        Assert.Equal(2, _store.Blobs.Count);
    }

    [Fact]
    public async Task Create_GpsFromPhotoWinsOverGeocoder()
    {
        _processor.Gps = new GeoPoint(10, 20);
        _geocoder.Result = new GeoPoint(1, 2);

        await _sut.CreateAsync(Input(), Image(), Image(), TestContext.Current.CancellationToken);

        Assert.Equal(10, Assert.Single(_repo.Labels.Values).Latitude);
        Assert.Equal(0, _geocoder.Calls);
    }

    [Fact]
    public async Task Create_NoCoordinates_UsesGeocoder()
    {
        _geocoder.Result = new GeoPoint(1.5, 2.5);

        await _sut.CreateAsync(Input(), Image(), Image(), TestContext.Current.CancellationToken);

        var label = Assert.Single(_repo.Labels.Values);
        Assert.Equal((1.5, 2.5), (label.Latitude, label.Longitude));
    }

    [Fact]
    public async Task Create_RepositoryFails_DeletesUploadedBlobs()
    {
        _repo.FailWrites = true;

        await Assert.ThrowsAsync<IOException>(() =>
            _sut.CreateAsync(Input(), Image(), Image(), TestContext.Current.CancellationToken));

        Assert.Empty(_store.Blobs);
    }

    [Fact]
    public async Task Create_UploadFailsMidway_DeletesAlreadyUploadedBlobs()
    {
        _store.FailOnUploadNumber = 3;

        await Assert.ThrowsAsync<IOException>(() =>
            _sut.CreateAsync(Input(), Image(), Image(), TestContext.Current.CancellationToken));

        Assert.Empty(_store.Blobs);
        Assert.Empty(_repo.Labels);
    }

    [Fact]
    public async Task Update_UnknownId_ReturnsFalseWithoutWrites()
    {
        var found = await _sut.UpdateAsync("nope", Input(), Image(), null, TestContext.Current.CancellationToken);

        Assert.False(found);
        Assert.Empty(_log);
    }

    [Fact]
    public async Task Update_TextOnly_KeepsImagesAndCoordinates()
    {
        Seed(Existing());

        var found = await _sut.UpdateAsync("id1", Input(), null, null, TestContext.Current.CancellationToken);

        Assert.True(found);
        var label = _repo.Labels["id1"];
        Assert.Equal("Cafe", label.Place);
        Assert.Equal("Česko", label.Country);
        Assert.Equal("id1_men.jpg", label.MenImage);
        Assert.Equal("id1_men_thumb.webp", label.MenThumb);
        Assert.Equal((50.0, 14.0), (label.Latitude, label.Longitude));
        Assert.Equal(["repo:update"], _log);
    }

    [Fact]
    public async Task Update_ChangedExtension_DeletesOldOriginalAfterUpdate()
    {
        Seed(Existing());

        await _sut.UpdateAsync("id1", Input(), Image("x.png"), null, TestContext.Current.CancellationToken);

        Assert.Equal("id1_men.png", _repo.Labels["id1"].MenImage);
        Assert.Equal("id1_men_thumb.webp", _repo.Labels["id1"].MenThumb);
        Assert.DoesNotContain("id1_men.jpg", _store.Blobs.Keys);
        Assert.Contains("id1_men.png", _store.Blobs.Keys);
        Assert.Contains("id1_women.jpg", _store.Blobs.Keys);
        Assert.True(_log.IndexOf("repo:update") < _log.IndexOf("delete:id1_men.jpg"));
        Assert.DoesNotContain("delete:id1_men_thumb.webp", _log);
    }

    [Fact]
    public async Task Update_SameExtension_OverwritesWithoutDeleting()
    {
        Seed(Existing());

        await _sut.UpdateAsync("id1", Input(), Image("x.jpg"), null, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(_log, l => l.StartsWith("delete:", StringComparison.Ordinal));
        Assert.Equal("image/jpeg", _store.Blobs["id1_men.jpg"]);
    }

    [Fact]
    public async Task Update_ThumbnailFailure_ClearsAndDeletesStaleThumb()
    {
        Seed(Existing());
        _processor.ThumbFails = true;

        await _sut.UpdateAsync("id1", Input(), Image("x.jpg"), null, TestContext.Current.CancellationToken);

        Assert.Equal("", _repo.Labels["id1"].MenThumb);
        Assert.DoesNotContain("id1_men_thumb.webp", _store.Blobs.Keys);
        Assert.Contains("id1_women_thumb.webp", _store.Blobs.Keys);
    }

    [Fact]
    public async Task Update_UnreadableImage_ThrowsAndWritesNothing()
    {
        Seed(Existing());

        await Assert.ThrowsAsync<InvalidImageException>(() =>
            _sut.UpdateAsync("id1", Input(), null, Image(data: Garbage), TestContext.Current.CancellationToken));

        Assert.Empty(_log);
    }

    [Fact]
    public async Task Update_ManualCoordinatesWin()
    {
        Seed(Existing());
        _processor.Gps = new GeoPoint(10, 20);

        await _sut.UpdateAsync("id1", Input("48.5", "14.3"), Image(), null, TestContext.Current.CancellationToken);

        Assert.Equal((48.5, 14.3), (_repo.Labels["id1"].Latitude, _repo.Labels["id1"].Longitude));
    }

    [Fact]
    public async Task Update_NewPhotoWithoutGps_KeepsExistingCoordinates()
    {
        Seed(Existing());

        await _sut.UpdateAsync("id1", Input(), Image(), null, TestContext.Current.CancellationToken);

        Assert.Equal((50.0, 14.0), (_repo.Labels["id1"].Latitude, _repo.Labels["id1"].Longitude));
        Assert.Equal(0, _geocoder.Calls);
    }

    [Fact]
    public async Task Update_NoCoordinatesAnywhere_Geocodes()
    {
        Seed(Existing() with { Latitude = null, Longitude = null });
        _geocoder.Result = new GeoPoint(7, 8);

        await _sut.UpdateAsync("id1", Input(), null, null, TestContext.Current.CancellationToken);

        Assert.Equal((7.0, 8.0), (_repo.Labels["id1"].Latitude, _repo.Labels["id1"].Longitude));
    }

    [Fact]
    public async Task Update_RepositoryFails_RemovesNewBlobsAndKeepsOldOnes()
    {
        Seed(Existing());
        _repo.FailWrites = true;

        await Assert.ThrowsAsync<IOException>(() =>
            _sut.UpdateAsync("id1", Input(), Image("x.png"), null, TestContext.Current.CancellationToken));

        Assert.DoesNotContain("id1_men.png", _store.Blobs.Keys);
        Assert.Contains("id1_men.jpg", _store.Blobs.Keys);
        // Náhled se stejným jménem už byl přepsán, ale label na něj dál odkazuje, takže se nemaže.
        Assert.Contains("id1_men_thumb.webp", _store.Blobs.Keys);
        Assert.Equal("id1_men.jpg", _repo.Labels["id1"].MenImage);
    }

    [Fact]
    public async Task Update_DeletingOldBlobFails_StillSucceeds()
    {
        Seed(Existing());
        _store.FailDeletes = true;

        var found = await _sut.UpdateAsync("id1", Input(), Image("x.png"), null, TestContext.Current.CancellationToken);

        Assert.True(found);
        Assert.Equal("id1_men.png", _repo.Labels["id1"].MenImage);
    }

    private sealed class FakeStore(List<string> log) : IImageStore
    {
        private int _uploads;

        public Dictionary<string, string> Blobs { get; } = [];
        public int FailOnUploadNumber { get; set; }
        public bool FailDeletes { get; set; }

        public Task UploadAsync(string name, byte[] data, string contentType, CancellationToken ct)
        {
            if (++_uploads == FailOnUploadNumber)
                throw new IOException("upload failed");
            log.Add("upload:" + name);
            Blobs[name] = contentType;
            return Task.CompletedTask;
        }

        public Task DeleteIfExistsAsync(string name, CancellationToken ct)
        {
            if (FailDeletes)
                throw new IOException("delete failed");
            log.Add("delete:" + name);
            Blobs.Remove(name);
            return Task.CompletedTask;
        }

        public string PublicUrl(string name) => "http://blob.test/" + name;
    }

    private sealed class FakeRepository(List<string> log) : ILabelRepository
    {
        public Dictionary<string, Label> Labels { get; } = [];
        public bool FailWrites { get; set; }

        public Task<IReadOnlyList<Label>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Label>>(Labels.Values.ToList());

        public Task<Label?> GetAsync(string id, CancellationToken ct) =>
            Task.FromResult(Labels.GetValueOrDefault(id));

        public Task CreateAsync(Label label, CancellationToken ct)
        {
            if (FailWrites)
                throw new IOException("table failed");
            log.Add("repo:create");
            Labels[label.Id] = label;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Label label, CancellationToken ct)
        {
            if (FailWrites)
                throw new IOException("table failed");
            log.Add("repo:update");
            Labels[label.Id] = label;
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<TableEntity> ListRawAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class FakeProcessor : IImageProcessor
    {
        public string ContentType { get; set; } = "image/jpeg";
        public GeoPoint? Gps { get; set; }
        public bool ThumbFails { get; set; }

        public ImageInfo? Inspect(byte[] data) => data.Length == 0 ? null : new ImageInfo(ContentType, Gps);

        public byte[]? MakeThumbnail(byte[] data) => ThumbFails ? null : [9];
    }
}
