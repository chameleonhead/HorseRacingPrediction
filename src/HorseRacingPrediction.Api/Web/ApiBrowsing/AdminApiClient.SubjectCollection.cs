using HorseRacingPrediction.Contracts.Subjects;

namespace HorseRacingPrediction.Api.Web.ApiBrowsing;

public sealed partial class AdminApiClient
{
    public Task<JraSubjectProfileDto?> GetJraSubjectProfileAsync(string kind, string id, CancellationToken token = default)
        => GetSubjectProfileAsync(kind, id, token);

    private async Task<JraSubjectProfileDto?> GetSubjectProfileAsync(string kind, string id, CancellationToken token)
        => (await GetJsonAsync<GetSubjectProfileResponse>(
            $"/api/v2/admin/subjects/{kind}/{Uri.EscapeDataString(id)}/profiles/current", token).ConfigureAwait(false))?.Profile;
}
