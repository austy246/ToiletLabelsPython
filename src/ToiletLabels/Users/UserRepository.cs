using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using ToiletLabels.Storage;

namespace ToiletLabels.Users;

public interface IUserRepository
{
    /// <summary>Uživatel podle jména (bez ohledu na velikost písmen), nebo <c>null</c>.</summary>
    Task<AppUser?> GetAsync(string userName, CancellationToken cancellationToken);

    /// <summary>Založí uživatele; <c>false</c>, když uživatel se stejným jménem (bez ohledu na velikost písmen) už existuje.</summary>
    Task<bool> TryCreateAsync(AppUser user, CancellationToken cancellationToken);
}

public sealed class UserRepository(TableServiceClient tables, IOptions<StorageOptions> options) : IUserRepository
{
    private const string UserPartition = "user";

    private readonly TableClient _table = tables.GetTableClient(options.Value.UsersTable);

    public async Task<AppUser?> GetAsync(string userName, CancellationToken cancellationToken)
    {
        var rowKey = RowKeyOf(userName);
        if (!TableKey.IsValid(rowKey))
            return null;
        try
        {
            var result = await _table.GetEntityIfExistsAsync<TableEntity>(
                UserPartition, rowKey, cancellationToken: cancellationToken);
            return result.HasValue ? FromEntity(result.Value!) : null;
        }
        catch (RequestFailedException ex) when (ex.Status is 400 or 404)
        {
            // Neplatný klíč (např. příliš dlouhé jméno) je pro volajícího totéž co neexistující uživatel.
            return null;
        }
    }

    public async Task<bool> TryCreateAsync(AppUser user, CancellationToken cancellationToken)
    {
        var rowKey = RowKeyOf(user.UserName);
        if (!TableKey.IsValid(rowKey))
            throw new ArgumentException($"User name '{user.UserName}' cannot be used as a table key.", nameof(user));

        var entity = new TableEntity(UserPartition, rowKey)
        {
            ["UserName"] = user.UserName,
            ["PasswordHash"] = user.PasswordHash,
            ["IsAdmin"] = user.IsAdmin,
            ["Created"] = user.Created,
        };
        try
        {
            await _table.AddEntityAsync(entity, cancellationToken);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            return false;
        }
    }

    private static string RowKeyOf(string userName) => userName.ToLowerInvariant();

    private static AppUser FromEntity(TableEntity entity) => new(
        entity.GetString("UserName") ?? entity.RowKey,
        entity.GetString("PasswordHash") ?? "",
        entity.GetBoolean("IsAdmin") ?? false,
        entity.GetString("Created") ?? "");
}
