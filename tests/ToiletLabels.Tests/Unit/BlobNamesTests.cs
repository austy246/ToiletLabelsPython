using ToiletLabels.Images;

namespace ToiletLabels.Tests.Unit;

public class BlobNamesTests
{
    [Fact]
    public void Original_KeepsExtensionAndCase()
    {
        Assert.Equal("id1_men.JPG", BlobNames.Original("id1", "men", "IMG_1.JPG"));
        Assert.Equal("id1_women.heic", BlobNames.Original("id1", "women", "photo.heic"));
    }

    [Fact]
    public void Thumb_ReplacesExtension()
    {
        Assert.Equal("id1_men_thumb.webp", BlobNames.Thumb("id1_men.JPG"));
    }

    [Fact]
    public void Thumb_WithoutExtension_AppendsSuffix()
    {
        Assert.Equal("id1_men_thumb.webp", BlobNames.Thumb("id1_men"));
    }

    [Theory]
    [InlineData("IMG")]
    [InlineData("")]
    [InlineData("archive.tar.gz")]
    public void Original_UsesOnlyLastExtension(string fileName)
    {
        var expected = fileName == "archive.tar.gz" ? "id1_men.gz" : "id1_men";
        Assert.Equal(expected, BlobNames.Original("id1", "men", fileName));
    }

    [Theory]
    [InlineData("photo.j pg")]
    [InlineData("photo.jp#g")]
    [InlineData("photo.jp?g")]
    [InlineData("photo.čeština")]
    [InlineData("photo.abcdefghijklmnop")]
    public void Original_DropsUnsafeExtension(string fileName)
    {
        // Přípona se dostane do veřejné URL, takže cokoli mimo krátké ASCII alfanumerické přípony se zahodí.
        Assert.Equal("id1_men", BlobNames.Original("id1", "men", fileName));
    }

    [Fact]
    public void Original_IgnoresDirectoryPartOfFileName()
    {
        Assert.Equal("id1_men.png", BlobNames.Original("id1", "men", @"C:\fakepath\a.b\x.png"));
        Assert.Equal("id1_men", BlobNames.Original("id1", "men", "dir.d/noext"));
    }
}
