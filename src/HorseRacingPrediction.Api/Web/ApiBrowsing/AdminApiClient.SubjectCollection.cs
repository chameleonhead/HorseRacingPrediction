using HorseRacingPrediction.Contracts;
namespace HorseRacingPrediction.Api.Web.ApiBrowsing;

public sealed partial class AdminApiClient
{
    public Task<JraSubjectProfileDto?> GetJraSubjectProfileAsync(string kind, string id, CancellationToken token = default)
        => GetJsonAsync<JraSubjectProfileDto>($"/api/admin/subjects/{kind}/{Uri.EscapeDataString(id)}/profile", token);
}
