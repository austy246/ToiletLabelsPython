using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using ToiletLabels.Storage;

namespace ToiletLabels.Users;

public static class AuthSetup
{
    public const string AdminPolicy = "Admin";
    public const string CookieName = "tl_auth";

    public static IServiceCollection AddAppAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AdminOptions>().BindConfiguration(AdminOptions.SectionName);
        services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
        services.AddSingleton<IUserRepository, UserRepository>();
        services.AddSingleton<AdminSeeder>();
        services.AddSingleton(TimeProvider.System);

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.LoginPath = "/login";
                options.ReturnUrlParameter = "next";
                options.ExpireTimeSpan = TimeSpan.FromDays(14);
                options.SlidingExpiration = true;
                // Přihlášený, ale neoprávněný uživatel dostane 403, ne přesměrování na přihlášení.
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });
        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<IWebHostEnvironment>((options, env) =>
                options.Cookie.SecurePolicy = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always);

        // Antiforgery cookie: HttpOnly zůstává výchozí, mimo Development navíc vždy Secure (provoz jde přes HTTPS za Caddy).
        services.AddOptions<AntiforgeryOptions>()
            .Configure<IWebHostEnvironment>((options, env) =>
            {
                if (!env.IsDevelopment())
                    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AdminPolicy, policy => policy.RequireClaim(AppClaims.IsAdmin, "true"));

        // Klíče pro cookie a antiforgery jsou v blobu, aby přežily restart i nasazení nového kontejneru.
        var storage = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
        var connectionString = configuration.GetConnectionString("Storage")
            ?? throw new InvalidOperationException("Missing configuration value ConnectionStrings:Storage.");
        services.AddDataProtection()
            .SetApplicationName("ToiletLabels")
            .PersistKeysToAzureBlobStorage(connectionString, storage.KeysContainer, "keys.xml");

        // Provoz chodí jen přes Caddy v Docker síti, takže hlavičkám X-Forwarded-* věříme od libovolného proxy.
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        return services;
    }
}
