using HorseRacingPrediction.Collector.Scheduling;
using HorseRacingPrediction.Contracts;
namespace HorseRacingPrediction.Api.Web.ApiBrowsing;

public sealed partial class AdminApiClient
{
    public Task<JraSubjectProfileDto?> GetJraSubjectProfileAsync(string kind, string id, CancellationToken token = default)
        => GetJsonAsync<JraSubjectProfileDto>($"/api/admin/subjects/{kind}/{Uri.EscapeDataString(id)}/profile", token);
    public Task<SubjectCollectionStatus?> GetSubjectCollectionAsync(string kind, string id, string operation, CancellationToken token = default)
        => GetJsonAsync<SubjectCollectionStatus>($"/api/admin/subjects/{kind}/{Uri.EscapeDataString(id)}/collection/{operation}", token);
    public Task<AdminApiResult> RequestSubjectCollectionAsync(string kind, string id, string operation, bool retry, CancellationToken token = default)
        => SendAsync(HttpMethod.Post, $"/api/admin/subjects/{kind}/{Uri.EscapeDataString(id)}/collection/{operation}" + (retry ? "/retry" : ""), new { }, token);
}
