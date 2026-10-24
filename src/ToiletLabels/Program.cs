using Azure.Data.Tables;
using Azure.Storage.Blobs;
using ToiletLabels.Cli;
using ToiletLabels.Labels;
using ToiletLabels.Storage;
using ToiletLabels.Users;

// Přepínače příkazové řádky se zpracují před buildem hostu.
if (args.Contains("--healthcheck"))
    return await HealthCheckCommand.RunAsync();

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();

builder.Services.AddOptions<StorageOptions>().BindConfiguration(StorageOptions.SectionName);
builder.Services.AddSingleton(sp => new TableServiceClient(StorageConnectionString(sp)));
builder.Services.AddSingleton(sp => new BlobServiceClient(StorageConnectionString(sp)));
builder.Services.AddSingleton<StorageBootstrapper>();
builder.Services.AddSingleton<ILabelRepository, LabelRepository>();
builder.Services.AddAppAuth(builder.Configuration);

var app = builder.Build();

await app.Services.GetRequiredService<StorageBootstrapper>()
    .EnsureAsync(app.Environment.IsDevelopment(), app.Lifetime.ApplicationStopping);
await app.Services.GetRequiredService<AdminSeeder>().SeedAsync(app.Lifetime.ApplicationStopping);

// Musí být první, aby ostatní middleware viděl skutečné schéma a IP klienta za Caddy.
app.UseForwardedHeaders();
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
