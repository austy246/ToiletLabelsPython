using System.Net.Http.Headers;

namespace ToiletLabels.Tests;

/// <summary>Soubor v multipart formuláři.</summary>
public sealed record FormFile(string Field, string FileName, byte[] Data, string ContentType = "image/jpeg");

public static class LabelFormClient
{
    /// <summary>
    /// Sestaví multipart formulář s antiforgery tokenem z GET téže stránky
    /// (token je vázaný na přihlášeného uživatele).
    /// </summary>
    public static async Task<MultipartFormDataContent> BuildFormAsync(
        this HttpClient client, string url, IDictionary<string, string>? fields, params FormFile[] files)
    {
        var token = await client.GetAntiforgeryTokenAsync(url, TestContext.Current.CancellationToken);
        var form = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
        foreach (var (name, value) in fields ?? new Dictionary<string, string>())
            form.Add(new StringContent(value), name);
        foreach (var file in files)
        {
            var content = new ByteArrayContent(file.Data);
            content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
            if (file.FileName.Length == 0)
            {
                // Prohlížeč bez vybraného souboru pošle část s prázdným filename; MultipartFormDataContent.Add to nedovolí.
                content.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
                {
                    Name = $"\"{file.Field}\"",
                    FileName = "\"\"",
                };
                form.Add(content);
            }
            else
            {
                form.Add(content, file.Field, file.FileName);
            }
        }

        return form;
    }

    public static async Task<HttpResponseMessage> PostFormAsync(
        this HttpClient client, string url, IDictionary<string, string>? fields, params FormFile[] files)
    {
        using var form = await client.BuildFormAsync(url, fields, files);
        return await client.PostAsync(url, form, TestContext.Current.CancellationToken);
    }

    public static async Task<string> BodyAsync(this HttpResponseMessage response) =>
        await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
}

/// <summary>Obsah bez předem známé délky, takže požadavek jde bez Content-Length (chunked).</summary>
public sealed class UnknownLengthContent(byte[] data) : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
        stream.WriteAsync(data, 0, data.Length);

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}
