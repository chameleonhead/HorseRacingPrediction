using HorseRacingPrediction.Contracts.Identity;

namespace HorseRacingPrediction.Api.Web.ApiBrowsing;

public sealed partial class AdminApiClient
{
    private const string IdentityMigrationPreflightPath = "/api/v2/admin/identity-migration-preflight";

    public Task<IdentityMigrationPreflightPage?> GetIdentityMigrationPreflightAsync(
        string? cursor,
        CancellationToken cancellationToken = default)
        => GetIdentityMigrationPreflightCoreAsync(cursor, cancellationToken);

    private async Task<IdentityMigrationPreflightPage?> GetIdentityMigrationPreflightCoreAsync(
        string? cursor,
        CancellationToken cancellationToken)
    {
        var path = cursor is null
            ? IdentityMigrationPreflightPath
            : $"{IdentityMigrationPreflightPath}?cursor={Uri.EscapeDataString(cursor)}";
        return (await GetJsonAsync<IdentityMigrationPreflightResponse>(path, cancellationToken)
            .ConfigureAwait(false))?.Page;
    }
}
