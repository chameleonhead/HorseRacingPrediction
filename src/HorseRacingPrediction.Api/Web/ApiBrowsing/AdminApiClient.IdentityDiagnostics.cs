using HorseRacingPrediction.Contracts.Identity;

namespace HorseRacingPrediction.Api.Web.ApiBrowsing;

public sealed partial class AdminApiClient
{
    public Task<HorseIdentityDiagnosticDto?> GetHorseIdentityDiagnosticAsync(
        string horseId,
        CancellationToken cancellationToken = default)
        => GetHorseIdentityDiagnosticCoreAsync(horseId, cancellationToken);

    private async Task<HorseIdentityDiagnosticDto?> GetHorseIdentityDiagnosticCoreAsync(
        string horseId,
        CancellationToken cancellationToken)
        => (await GetJsonAsync<GetHorseIdentityDiagnosticResponse>(
            $"/api/v2/admin/identity-diagnostics/horses/{Uri.EscapeDataString(horseId)}",
            cancellationToken).ConfigureAwait(false))?.Diagnostic;
}
