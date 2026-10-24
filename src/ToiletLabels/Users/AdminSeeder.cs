using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using ToiletLabels.Labels;

namespace ToiletLabels.Users;

/// <summary>Sekce <c>Admin</c> v konfiguraci: účet, který se při startu založí, pokud ještě neexistuje.</summary>
public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    public string? UserName { get; set; }
    public string? Password { get; set; }
}

/// <summary>
/// Při startu založí administrátora z konfigurace (stejně jako dřívější docker-entrypoint.sh u Djanga).
/// Existujícímu uživateli heslo nikdy nemění.
/// </summary>
public sealed class AdminSeeder(
    IUserRepository users,
    IPasswordHasher<AppUser> hasher,
    IOptions<AdminOptions> options,
    TimeProvider time)
{
    private readonly AdminOptions _options = options.Value;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.UserName) || string.IsNullOrEmpty(_options.Password))
            return;

        if (await users.GetAsync(_options.UserName, cancellationToken) is not null)
            return;

        var admin = new AppUser(_options.UserName, "", IsAdmin: true, CreatedTimestamp.Now(time));
        await users.TryCreateAsync(admin with { PasswordHash = hasher.HashPassword(admin, _options.Password) }, cancellationToken);
    }
}
