using System.Globalization;
using ToiletLabels.Geo;

namespace ToiletLabels.Tests.Unit;

public class ManualCoordinatesTests
{
    [Theory]
    [InlineData("50.0875", "14.4213", true)]
    [InlineData("50,0875", "14,4213", false)]
    [InlineData("", "14", false)]
    [InlineData("50", "", false)]
    [InlineData("91", "0", false)]
    [InlineData("-91", "0", false)]
    [InlineData("0", "181", false)]
    [InlineData("0", "-181", false)]
    [InlineData("90", "180", true)]
    [InlineData("-90", "-180", true)]
    [InlineData("NaN", "0", false)]
    [InlineData("0", "Infinity", false)]
    [InlineData("abc", "14", false)]
    [InlineData(" 50.1 ", " 14.2 ", true)]
    public void Parse_Cases(string lat, string lon, bool ok)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { "cs-CZ", "en-US" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                Assert.Equal(ok, ManualCoordinates.Parse(lat, lon).HasValue);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Parse_NullInput_Null()
    {
        Assert.Null(ManualCoordinates.Parse(null, "14"));
        Assert.Null(ManualCoordinates.Parse("50", null));
    }

    [Fact]
    public void Parse_RoundsToSixDecimals()
    {
        Assert.Equal(new GeoPoint(50.123457, 14.000001), ManualCoordinates.Parse("50.1234567", "14.0000014"));
    }
}
