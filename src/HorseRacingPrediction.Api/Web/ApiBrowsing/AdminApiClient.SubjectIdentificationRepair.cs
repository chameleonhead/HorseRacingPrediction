using System.Net.Http.Json;

using HorseRacingPrediction.Contracts.Repairs;

namespace HorseRacingPrediction.Api.Web.ApiBrowsing;

public sealed partial class AdminApiClient
{
    private const string SubjectIdentificationRepairPath = "/api/admin/repairs/subject-identification";

    public Task<SubjectIdentificationRepairPreviewDto?> GetSubjectIdentificationRepairPreviewAsync(
        CancellationToken cancellationToken = default)
        => GetSubjectIdentificationRepairPreviewCoreAsync(cancellationToken);

    private async Task<SubjectIdentificationRepairPreviewDto?> GetSubjectIdentificationRepairPreviewCoreAsync(CancellationToken token)
        => (await GetJsonAsync<GetSubjectIdentificationRepairResponse>(SubjectIdentificationRepairPath, token).ConfigureAwait(false))?.Preview;

    public async Task<AdminApiResult<SubjectIdentificationCandidateApplicationDto>>
        ApplySubjectIdentificationCandidateAsync(Guid notificationId,
            SubjectIdentificationCandidateSelectionInputDto selection,
            CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"{SubjectIdentificationRepairPath}/candidate/apply",
            new ApplySubjectIdentificationCandidateRequest(notificationId, selection),
            JsonOptions, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return AdminApiResult<SubjectIdentificationCandidateApplicationDto>.Fail(
                await ReadErrorsAsync(response, cancellationToken).ConfigureAwait(false));

        var value = await response.Content.ReadFromJsonAsync<ApplySubjectIdentificationCandidateResponse>(
            JsonOptions, cancellationToken).ConfigureAwait(false);
        return value is null
            ? AdminApiResult<SubjectIdentificationCandidateApplicationDto>.Fail(["復旧結果を確認できませんでした。"])
            : AdminApiResult<SubjectIdentificationCandidateApplicationDto>.Ok(value.Application);
    }

    public async Task<AdminApiResult<SubjectIdentificationExecutionDto>>
        ExecuteSubjectIdentificationRepairAsync(
            IReadOnlyList<SubjectIdentificationRepairInputDto> items,
            CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"{SubjectIdentificationRepairPath}/execute",
            new ExecuteSubjectIdentificationRepairRequest(items), JsonOptions, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return AdminApiResult<SubjectIdentificationExecutionDto>.Fail(
                await ReadErrorsAsync(response, cancellationToken).ConfigureAwait(false));

        var value = await response.Content.ReadFromJsonAsync<ExecuteSubjectIdentificationRepairResponse>(
            JsonOptions, cancellationToken).ConfigureAwait(false);
        return value is null
            ? AdminApiResult<SubjectIdentificationExecutionDto>.Fail(["補正結果を確認できませんでした。"])
            : AdminApiResult<SubjectIdentificationExecutionDto>.Ok(value.Execution);
    }

    public async Task<AdminApiResult<SubjectIdentificationDismissalDto>>
        DismissSubjectIdentificationFailuresAsync(
            IReadOnlyList<Guid> notificationIds, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"{SubjectIdentificationRepairPath}/dismiss",
            new DismissSubjectIdentificationFailuresRequest(notificationIds), JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return AdminApiResult<SubjectIdentificationDismissalDto>.Fail(
                await ReadErrorsAsync(response, cancellationToken).ConfigureAwait(false));

        var value = await response.Content.ReadFromJsonAsync<DismissSubjectIdentificationFailuresResponse>(
            JsonOptions, cancellationToken).ConfigureAwait(false);
        return value is null
            ? AdminApiResult<SubjectIdentificationDismissalDto>.Fail(["対応不要化の結果を確認できませんでした。"])
            : AdminApiResult<SubjectIdentificationDismissalDto>.Ok(value.Dismissal);
    }
}
