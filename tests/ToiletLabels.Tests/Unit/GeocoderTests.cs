using System.Net;
using System.Text;
using ToiletLabels.Geo;

namespace ToiletLabels.Tests.Unit;

public class GeocoderTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (NominatimGeocoder Geocoder, StubHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        return (new NominatimGeocoder(new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task Geocode_BuildsNominatimRequest()
    {
        var (geocoder, handler) = Create(_ => Json("[]"));

        await geocoder.GeocodeAsync("Hlavní nádraží", "Praha", "Česko", TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "https://nominatim.openstreetmap.org/search?q=Hlavn%C3%AD%20n%C3%A1dra%C5%BE%C3%AD%2C%20Praha%2C%20%C4%8Cesko&format=json&limit=1",
            request.RequestUri!.AbsoluteUri);
        Assert.Equal("ToiletLabels/1.0 (hausterlitz@gmail.com)", request.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task Geocode_SkipsEmptyParts()
    {
        var (geocoder, handler) = Create(_ => Json("[]"));

        await geocoder.GeocodeAsync(" Kavárna ", "", " Brno ", TestContext.Current.CancellationToken);

        Assert.Contains("q=Kav%C3%A1rna%2C%20Brno&", Assert.Single(handler.Requests).RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Geocode_ParsesFirstResult()
    {
        var (geocoder, _) = Create(_ => Json("""[{"lat":"50.0833331","lon":"14.4166667"}]"""));

        var result = await geocoder.GeocodeAsync("Hlavní nádraží", "Praha", "Česko", TestContext.Current.CancellationToken);

        Assert.Equal(new GeoPoint(50.083333, 14.416667), result);
    }

    [Fact]
    public async Task Geocode_AllEmpty_NoRequest()
    {
        var (geocoder, handler) = Create(_ => Json("[]"));

        var result = await geocoder.GeocodeAsync("", "  ", "", TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Geocode_EmptyArray_Null()
    {
        var (geocoder, _) = Create(_ => Json("[]"));

        Assert.Null(await geocoder.GeocodeAsync("x", "", "", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Geocode_HttpError_Null()
    {
        var (geocoder, _) = Create(_ => throw new HttpRequestException("boom"));

        Assert.Null(await geocoder.GeocodeAsync("x", "", "", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Geocode_ErrorStatus_Null()
    {
        var (geocoder, _) = Create(_ => Json("""[{"lat":"1","lon":"2"}]""", HttpStatusCode.TooManyRequests));

        Assert.Null(await geocoder.GeocodeAsync("x", "", "", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"lat":"1","lon":"2"}""")]
    [InlineData("""[{"lat":"abc","lon":"2"}]""")]
    [InlineData("""[{"foo":"bar"}]""")]
    public async Task Geocode_MalformedBody_Null(string body)
    {
        var (geocoder, _) = Create(_ => Json(body));

        Assert.Null(await geocoder.GeocodeAsync("x", "", "", TestContext.Current.CancellationToken));
    }
}
