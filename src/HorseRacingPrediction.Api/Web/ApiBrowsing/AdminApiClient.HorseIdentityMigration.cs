using System.Net.Http.Json;
using HorseRacingPrediction.Contracts.Identity;

namespace HorseRacingPrediction.Api.Web.ApiBrowsing;

public sealed partial class AdminApiClient
{
    private const string HorseIdentityMigrationPreviewPath = "/api/v2/admin/horse-id-migration-previews";
    private const string HorseIdentityMigrationOperationsPath = "/api/v2/admin/horse-id-migration-operations";

    public Task<HorseIdentityMigrationPreview?> PreviewHorseIdentityMigrationAsync(
        HorseIdentityMigrationManifest manifest, CancellationToken cancellationToken = default)
        => PostJsonAsync<HorseIdentityMigrationPreview>(HorseIdentityMigrationPreviewPath, manifest, cancellationToken);

    public Task<HorseIdentityMigrationOperationResponse?> StageHorseIdentityMigrationAsync(
        HorseIdentityMigrationManifest manifest, string previewFingerprint,
        CancellationToken cancellationToken = default)
        => PostJsonAsync<HorseIdentityMigrationOperationResponse>(HorseIdentityMigrationOperationsPath,
            new HorseIdentityMigrationStageRequest(manifest, previewFingerprint), cancellationToken);

    public Task<HorseIdentityMigrationOperationResponse?> GetHorseIdentityMigrationOperationAsync(
        Guid operationId, CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty) return Task.FromResult<HorseIdentityMigrationOperationResponse?>(null);
        return GetJsonAsync<HorseIdentityMigrationOperationResponse>(
            $"{HorseIdentityMigrationOperationsPath}/{operationId:D}", cancellationToken);
    }

    private async Task<T?> PostJsonAsync<T>(string requestUri, object body, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(requestUri, body, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.NoContent)
            return default;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
    }
}
