using HorseRacingPrediction.Contracts.Subjects;

namespace HorseRacingPrediction.Api.Web.ApiBrowsing;

public sealed partial class AdminApiClient
{
    public Task<JraSubjectProfileDto?> GetJraSubjectProfileAsync(string kind, string id, CancellationToken token = default)
        => GetJsonAsync<JraSubjectProfileDto>($"/api/v2/admin/subjects/{kind}/{Uri.EscapeDataString(id)}/profiles/current", token);
}
