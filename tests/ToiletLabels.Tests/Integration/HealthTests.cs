using System.Net;
using ToiletLabels.Cli;

namespace ToiletLabels.Tests.Integration;

public class HealthTests(AzuriteFixture azurite)
{
    [Fact]
    public async Task Healthz_ReturnsOk()
    {
        await using var factory = new AppFactory(azurite);
        var client = factory.CreateClient();

        var res = await client.GetAsync("/healthz", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("{\"status\":\"ok\"}", await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HealthCheckCommand_ReturnsOneWhenNothingListens()
    {
        var exitCode = await HealthCheckCommand.RunAsync("http://127.0.0.1:1/healthz");

        Assert.Equal(1, exitCode);
    }
}
