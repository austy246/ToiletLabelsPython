using ImageMagick;
using ToiletLabels.Geo;
using ToiletLabels.Images;
using ToiletLabels.Tests.Infrastructure;

namespace ToiletLabels.Tests.Unit;

public class ImageProcessorTests
{
    private readonly ImageProcessor _sut = new();

    private static (uint Width, uint Height, MagickFormat Format) Describe(byte[] data)
    {
        var info = new MagickImageInfo(data);
        return (info.Width, info.Height, info.Format);
    }

    private static byte[] Heic() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.heic"));

    [Fact]
    public void MakeThumbnail_ProducesWebpWithin400()
    {
        var thumb = _sut.MakeThumbnail(TestImages.Jpeg(1200, 800));

        Assert.NotNull(thumb);
        var (w, h, format) = Describe(thumb);
        Assert.Equal(MagickFormat.WebP, format);
        Assert.Equal(400u, w);
        Assert.InRange(h, 266u, 268u);
    }

    [Fact]
    public void MakeThumbnail_DoesNotUpscale()
    {
        var thumb = _sut.MakeThumbnail(TestImages.Jpeg(200, 100));

        Assert.NotNull(thumb);
        var (w, h, _) = Describe(thumb);
        Assert.Equal((200u, 100u), (w, h));
    }

    [Fact]
    public void MakeThumbnail_HandlesRgbaPng()
    {
        Assert.NotNull(_sut.MakeThumbnail(TestImages.PngRgba(500, 500)));
    }

    [Fact]
    public void MakeThumbnail_AppliesExifOrientation()
    {
        var thumb = _sut.MakeThumbnail(TestImages.Jpeg(800, 400, orientation: 6));

        Assert.NotNull(thumb);
        var (w, h, _) = Describe(thumb);
        Assert.Equal((200u, 400u), (w, h));
    }

    [Fact]
    public void MakeThumbnail_LargeImage_Fits400()
    {
        var thumb = _sut.MakeThumbnail(TestImages.Jpeg(8000, 6000));

        Assert.NotNull(thumb);
        var (w, h, _) = Describe(thumb);
        Assert.Equal((400u, 300u), (w, h));
    }

    [Fact]
    public void MakeThumbnail_Garbage_ReturnsNull()
    {
        Assert.Null(_sut.MakeThumbnail("not an image"u8.ToArray()));
    }

    [Fact]
    public void MakeThumbnail_Heic_Works()
    {
        var thumb = _sut.MakeThumbnail(Heic());

        Assert.NotNull(thumb);
        var (w, h, format) = Describe(thumb);
        Assert.Equal(MagickFormat.WebP, format);
        Assert.True(Math.Max(w, h) <= 400, $"{w}x{h}");
    }

    [Fact]
    public void Inspect_Jpeg_ContentTypeAndGps()
    {
        var info = _sut.Inspect(TestImages.Jpeg(100, 100, gps: (50.0875, -14.4213)));

        Assert.NotNull(info);
        Assert.Equal("image/jpeg", info.ContentType);
        Assert.Equal(new GeoPoint(50.0875, -14.4213), info.Gps);
    }

    [Fact]
    public void Inspect_SouthWest_Negative()
    {
        var info = _sut.Inspect(TestImages.Jpeg(100, 100, gps: (-33.8688, -151.2093)));

        Assert.NotNull(info);
        Assert.Equal(new GeoPoint(-33.8688, -151.2093), info.Gps);
    }

    [Fact]
    public void Inspect_NoExif_NullGps()
    {
        var info = _sut.Inspect(TestImages.Jpeg(100, 100));

        Assert.NotNull(info);
        Assert.Equal("image/jpeg", info.ContentType);
        Assert.Null(info.Gps);
    }

    [Fact]
    public void Inspect_Png_ContentType()
    {
        Assert.Equal("image/png", _sut.Inspect(TestImages.PngRgba(10, 10))?.ContentType);
    }

    [Fact]
    public void Inspect_Heic_ContentType()
    {
        Assert.Equal("image/heic", _sut.Inspect(Heic())?.ContentType);
    }

    [Fact]
    public void Inspect_Garbage_ReturnsNull()
    {
        Assert.Null(_sut.Inspect("not an image"u8.ToArray()));
    }

    [Fact]
    public void GeoPoint_Rounded_RoundsToSixPlaces()
    {
        Assert.Equal(new GeoPoint(50.0875, -14.4213), GeoPoint.Rounded(50.08750049, -14.42129951));
    }
}
