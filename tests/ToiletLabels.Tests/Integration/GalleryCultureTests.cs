using System.Globalization;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using ToiletLabels.Labels;

namespace ToiletLabels.Tests.Integration;

/// <summary>Skupina, která běží sama, protože test mění globální kulturu procesu.</summary>
[CollectionDefinition("Global culture", DisableParallelization = true)]
public sealed class GlobalCultureCollection;

[Collection("Global culture")]
public class GalleryCultureTests(AzuriteFixture azurite)
{
    [Fact]
    public async Task Index_UnderCzechCulture_UsesDotDecimals()
    {
        var ct = TestContext.Current.CancellationToken;
        var original = CultureInfo.DefaultThreadCurrentCulture;
        CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("cs-CZ");
        try
        {
            await using var factory = new AppFactory(azurite);
            await factory.Services.GetRequiredService<ILabelRepository>().CreateAsync(new Label
            {
                Id = "id1", Place = "Cafe", MenImage = "m.jpg", WomenImage = "w.jpg",
                Latitude = 50.08, Longitude = 14.42, NumVoters = 2, AvgVote = 4.5,
            }, ct);

            var response = await factory.CreateClient().GetAsync("/", ct);
            var html = await response.Content.ReadAsStringAsync(ct);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("center=14.42,50.08", html);
            Assert.Contains("\"lat\":50.08", html);
            Assert.Contains("4.5/5", html);
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentCulture = original;
        }
    }
}
