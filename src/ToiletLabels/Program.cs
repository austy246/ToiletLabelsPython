using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http.Features;
using ToiletLabels.Cli;
using ToiletLabels.Geo;
using ToiletLabels.Http;
using ToiletLabels.Images;
using ToiletLabels.Labels;
using ToiletLabels.Storage;
using ToiletLabels.Users;

// Přepínače příkazové řádky se zpracují před buildem hostu.
if (args.Contains("--healthcheck"))
    return await HealthCheckCommand.RunAsync();

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();

// Limit těla požadavku 50 MB (fotky z mobilu). Kestrel ho vynucuje sám; UseRequestBodyLimit ho hlídá navíc,
// protože TestServer limity nevynucuje a antiforgery výjimky při čtení formuláře mění na 400 místo 413.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = RequestBodyLimit.MaxBytes);
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = RequestBodyLimit.MaxBytes);

builder.Services.AddOptions<StorageOptions>().BindConfiguration(StorageOptions.SectionName);
builder.Services.AddOptions<MapyOptions>().BindConfiguration(MapyOptions.SectionName);
builder.Services.AddSingleton(sp => new TableServiceClient(StorageConnectionString(sp)));
builder.Services.AddSingleton(sp => new BlobServiceClient(StorageConnectionString(sp)));
builder.Services.AddSingleton<StorageBootstrapper>();
builder.Services.AddSingleton<ILabelRepository, LabelRepository>();
builder.Services.AddSingleton<IImageStore, BlobImageStore>();
builder.Services.AddSingleton<IImageProcessor, ImageProcessor>();
builder.Services.AddHttpClient<IGeocoder, NominatimGeocoder>();
builder.Services.AddScoped<LabelEditor>();
builder.Services.AddAppAuth(builder.Configuration);

var app = builder.Build();

await app.Services.GetRequiredService<StorageBootstrapper>()
    .EnsureAsync(app.Environment.IsDevelopment(), app.Lifetime.ApplicationStopping);
await app.Services.GetRequiredService<AdminSeeder>().SeedAsync(app.Lifetime.ApplicationStopping);

// Musí být první, aby ostatní middleware viděl skutečné schéma a IP klienta za Caddy.
app.UseForwardedHeaders();
app.UseRequestBodyLimit();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => Results.Json(new { status = "ok" }));
app.MapRazorPages();

app.Run();
return 0;

static string StorageConnectionString(IServiceProvider sp) =>
    sp.GetRequiredService<IConfiguration>().GetConnectionString("Storage")
    ?? throw new InvalidOperationException("Missing configuration value ConnectionStrings:Storage.");

public partial class Program;
