using HorseRacingPrediction.Contracts.Identity;

namespace HorseRacingPrediction.Api.Web.ApiBrowsing;

public sealed partial class AdminApiClient
{
    private const string HorseIdentityMigrationSchemaDiagnosticsPath =
        "/api/v2/admin/horse-id-migration-schema-diagnostics";

    public Task<HorseIdentityMigrationSchemaDiagnostics?> GetHorseIdentityMigrationSchemaDiagnosticsAsync(
        CancellationToken cancellationToken = default) =>
        GetJsonAsync<HorseIdentityMigrationSchemaDiagnostics>(HorseIdentityMigrationSchemaDiagnosticsPath,
            cancellationToken);
}
