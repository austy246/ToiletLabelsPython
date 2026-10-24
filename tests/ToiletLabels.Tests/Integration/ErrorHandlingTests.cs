using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace ToiletLabels.Tests.Integration;

public class ErrorHandlingTests(AzuriteFixture azurite)
{
    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task Production_UnhandledException_ReturnsCzechErrorPageWithoutDetails(string method)
    {
        await using var baseFactory = new AppFactory(azurite);
        await using var factory = baseFactory.WithWebHostBuilder(b => b.UseEnvironment("Production"));
        var client = factory.CreateClient();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), "/__throw"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Contains("Něco se pokazilo. Zkus to prosím znovu.", html);
        Assert.DoesNotContain("secret-internal-detail", html);
        Assert.DoesNotContain("InvalidOperationException", html);
        Assert.DoesNotContain(" at ToiletLabels", html);
    }

    [Fact]
    public async Task Production_UnknownPath_ReturnsPlain404()
    {
        await using var baseFactory = new AppFactory(azurite);
        await using var factory = baseFactory.WithWebHostBuilder(b => b.UseEnvironment("Production"));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/no-such-page", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.StartsWith("text/plain", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task AppFactory_RejectsNonLocalStorage()
    {
        await using var factory = new AppFactory(azurite).WithSetting("ConnectionStrings:Storage",
            "DefaultEndpointsProtocol=https;AccountName=prod;AccountKey=x;EndpointSuffix=core.windows.net");

        Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
    }
}
