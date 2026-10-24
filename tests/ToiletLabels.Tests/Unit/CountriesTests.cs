using ToiletLabels.Labels;

namespace ToiletLabels.Tests.Unit;

public class CountriesTests
{
    [Theory]
    [InlineData("cesko", "Česko")] [InlineData("Czech Republic", "Česko")] [InlineData("  ČESKÁ republika ", "Česko")]
    [InlineData("uk", "Spojené království")] [InlineData("Great Britain", "Spojené království")]
    [InlineData("švýcarsko", "Švýcarsko")] [InlineData("CZ", "CZ")] [InlineData("  Narnie ", "Narnie")]
    [InlineData("", "")] [InlineData(null, "")]
    public void Normalize_MapsToCanonical(string? input, string expected) => Assert.Equal(expected, Countries.Normalize(input));

    [Fact] public void All_HasPythonOrder() { Assert.Equal(18, Countries.All.Count); Assert.Equal("Česko", Countries.All[0]); Assert.Equal("Švýcarsko", Countries.All[17]); }
    [Fact] public void IsoCode_Works() { Assert.Equal("gb", Countries.IsoCode("United Kingdom")); Assert.Equal("cz", Countries.IsoCode("Česko")); Assert.Equal("", Countries.IsoCode("Narnie")); }
}
