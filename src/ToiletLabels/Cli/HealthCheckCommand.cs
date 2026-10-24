namespace ToiletLabels.Cli;

/// <summary>Režim <c>--healthcheck</c> pro Docker HEALTHCHECK: exit code 0 = HTTP 200, jinak 1.</summary>
public static class HealthCheckCommand
{
    public static async Task<int> RunAsync(string url = "http://localhost:8080/healthz")
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var response = await http.GetAsync(url);
            return response.StatusCode == System.Net.HttpStatusCode.OK ? 0 : 1;
        }
        catch (Exception)
        {
            return 1;
        }
    }
}
