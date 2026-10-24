using ToiletLabels.Cli;

// Přepínače příkazové řádky se zpracují před buildem hostu.
if (args.Contains("--healthcheck"))
    return await HealthCheckCommand.RunAsync();

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();

var app = builder.Build();

app.MapGet("/healthz", () => Results.Json(new { status = "ok" }));
app.MapRazorPages();

app.Run();
return 0;

public partial class Program;
