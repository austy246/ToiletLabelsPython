using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using ToiletLabels.Users;

namespace ToiletLabels.Tests.Integration;

public class AdminSeederTests(AzuriteFixture azurite)
{
    private static readonly PasswordHasher<AppUser> Hasher = new();

    private static async Task<(UserRepository Users, AdminSeeder Seeder)> CreateAsync(
        TestStorage storage, string? userName, string? password, CancellationToken ct)
    {
        await storage.UsersTableClient.CreateIfNotExistsAsync(ct);
        var users = new UserRepository(storage.Tables, Options.Create(storage.Options));
        var seeder = new AdminSeeder(
            users, Hasher, Options.Create(new AdminOptions { UserName = userName, Password = password }), TimeProvider.System);
        return (users, seeder);
    }

    private static bool PasswordMatches(AppUser user, string password) =>
        Hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;

    [Fact]
    public async Task Seed_CreatesAdminFromConfig()
    {
        var ct = TestContext.Current.CancellationToken;
        var (users, seeder) = await CreateAsync(new TestStorage(azurite), "austy", "pw-1", ct);

        await seeder.SeedAsync(ct);

        var user = await users.GetAsync("austy", ct);
        Assert.NotNull(user);
        Assert.Equal("austy", user.UserName);
        Assert.True(user.IsAdmin);
        Assert.True(PasswordMatches(user, "pw-1"));
        Assert.False(PasswordMatches(user, "other"));
        Assert.NotEqual("pw-1", user.PasswordHash);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{6}$", user.Created);
    }

    [Fact]
    public async Task Seed_DoesNotOverwriteExistingPassword()
    {
        var ct = TestContext.Current.CancellationToken;
        var (users, seeder) = await CreateAsync(new TestStorage(azurite), "austy", "pw-new", ct);
        var existing = new AppUser("austy", "", IsAdmin: false, Created: "2020-01-01T00:00:00.000000");
        Assert.True(await users.TryCreateAsync(
            existing with { PasswordHash = Hasher.HashPassword(existing, "pw-old") }, ct));

        await seeder.SeedAsync(ct);

        var user = await users.GetAsync("austy", ct);
        Assert.NotNull(user);
        Assert.True(PasswordMatches(user, "pw-old"));
        Assert.False(PasswordMatches(user, "pw-new"));
        Assert.False(user.IsAdmin);
    }

    [Theory]
    [InlineData("austy", null)]
    [InlineData("austy", "")]
    [InlineData(null, "pw-1")]
    [InlineData("", "pw-1")]
    public async Task Seed_MissingNameOrPassword_DoesNothing(string? userName, string? password)
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new TestStorage(azurite);
        var (users, seeder) = await CreateAsync(storage, userName, password, ct);

        await seeder.SeedAsync(ct);

        Assert.Null(await users.GetAsync("austy", ct));
        Assert.Empty(await storage.UsersTableClient.QueryAsync<Azure.Data.Tables.TableEntity>(cancellationToken: ct).ToListAsync(ct));
    }

    [Fact]
    public async Task UserName_IsCaseInsensitive()
    {
        var ct = TestContext.Current.CancellationToken;
        var (users, seeder) = await CreateAsync(new TestStorage(azurite), "Austy", "pw-1", ct);

        await seeder.SeedAsync(ct);

        var user = await users.GetAsync("AUSTY", ct);
        Assert.NotNull(user);
        Assert.Equal("Austy", user.UserName);
        Assert.NotNull(await users.GetAsync("austy", ct));
    }

    [Fact]
    public async Task TryCreate_ExistingUserNameInOtherCase_ReturnsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        var (users, _) = await CreateAsync(new TestStorage(azurite), null, null, ct);

        Assert.True(await users.TryCreateAsync(new AppUser("austy", "h1", true, "c"), ct));
        Assert.False(await users.TryCreateAsync(new AppUser("AUSTY", "h2", false, "c"), ct));

        Assert.Equal("h1", (await users.GetAsync("austy", ct))!.PasswordHash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("a#b")]
    public async Task Get_InvalidKeyCharacters_ReturnsNull(string userName)
    {
        var ct = TestContext.Current.CancellationToken;
        var (users, _) = await CreateAsync(new TestStorage(azurite), null, null, ct);

        Assert.Null(await users.GetAsync(userName, ct));
    }
}
