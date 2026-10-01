namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionWakeSignalDto(Guid WakeId, Guid DispatchEnvelopeId, string ReservationToken,
    int ContractVersion = 1)
{
    public const int CurrentContractVersion = 1;
    public bool IsSupported() => ContractVersion == CurrentContractVersion && WakeId != Guid.Empty
        && DispatchEnvelopeId != Guid.Empty && !string.IsNullOrWhiteSpace(ReservationToken);
}
