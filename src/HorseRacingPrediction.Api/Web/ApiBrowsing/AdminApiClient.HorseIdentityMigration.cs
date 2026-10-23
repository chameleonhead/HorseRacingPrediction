using System.Net.Http.Json;
using HorseRacingPrediction.Contracts.Identity;

namespace HorseRacingPrediction.Api.Web.ApiBrowsing;

public sealed partial class AdminApiClient
{
    private const string HorseIdentityMigrationPreviewPath = "/api/v2/admin/horse-id-migration-previews";
    private const string HorseIdentityMigrationOperationsPath = "/api/v2/admin/horse-id-migration-operations";
    private const string HorseIdentityMigrationRequestsPath = "/api/v2/admin/horse-id-migration-requests";

    public Task<HorseIdentityMigrationPreview?> PreviewHorseIdentityMigrationAsync(
        HorseIdentityMigrationManifest manifest, CancellationToken cancellationToken = default)
        => PostJsonAsync<HorseIdentityMigrationPreview>(HorseIdentityMigrationPreviewPath, manifest, cancellationToken);

    public Task<HorseIdentityMigrationTargetReview?> ReviewHorseIdentityMigrationTargetsAsync(
        HorseIdentityMigrationManifest manifest, CancellationToken cancellationToken = default)
        => PostJsonAsync<HorseIdentityMigrationTargetReview>(
            HorseIdentityMigrationPreviewPath + "?kind=TargetReviewV2", manifest, cancellationToken);

    public Task<HorseIdentityMigrationOperationResponse?> StageHorseIdentityMigrationAsync(
        HorseIdentityMigrationManifest manifest, string previewFingerprint,
        CancellationToken cancellationToken = default)
        => StageHorseIdentityMigrationAsync(manifest, previewFingerprint, null, cancellationToken);

    public Task<HorseIdentityMigrationOperationResponse?> StageHorseIdentityMigrationAsync(
        HorseIdentityMigrationManifest manifest, string previewFingerprint, Guid? requestKey,
        CancellationToken cancellationToken = default)
        => PostJsonAsync<HorseIdentityMigrationOperationResponse>(HorseIdentityMigrationOperationsPath,
            new HorseIdentityMigrationStageRequest(manifest, previewFingerprint) { RequestKey = requestKey },
            cancellationToken);

    public Task<HorseIdentityMigrationOperationResponse?> StagePendingHorseIdentityMigrationAsync(
        HorseIdentityMigrationManifest manifest, Guid? requestKey = null,
        CancellationToken cancellationToken = default)
        => PostJsonAsync<HorseIdentityMigrationOperationResponse>(HorseIdentityMigrationOperationsPath,
            new HorseIdentityMigrationStageRequest(manifest, null)
            {
                RequestKind = HorseIdentityMigrationAdmissionKind.PendingV2,
                RequestKey = requestKey,
            }, cancellationToken);

    public Task<HorseIdentityMigrationOperationResponse?> GetHorseIdentityMigrationRequestAsync(
        Guid requestKey, CancellationToken cancellationToken = default)
    {
        if (requestKey == Guid.Empty) return Task.FromResult<HorseIdentityMigrationOperationResponse?>(null);
        return GetJsonAsync<HorseIdentityMigrationOperationResponse>(
            $"{HorseIdentityMigrationRequestsPath}/{requestKey:D}", cancellationToken);
    }

    public Task<HorseIdentityMigrationOperationResponse?> GetHorseIdentityMigrationOperationAsync(
        Guid operationId, CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty) return Task.FromResult<HorseIdentityMigrationOperationResponse?>(null);
        return GetJsonAsync<HorseIdentityMigrationOperationResponse>(
            $"{HorseIdentityMigrationOperationsPath}/{operationId:D}", cancellationToken);
    }

    public async Task<HorseIdentityMigrationOperationResponse?> ExecuteHorseIdentityMigrationAsync(
        Guid operationId, CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty) return null;
        using var response = await _httpClient.PostAsync(
            $"{HorseIdentityMigrationOperationsPath}/{operationId:D}/execute", content: null, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.NoContent)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<HorseIdentityMigrationOperationResponse>(
            JsonOptions, cancellationToken).ConfigureAwait(false);
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
