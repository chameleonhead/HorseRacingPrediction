namespace HorseRacingPrediction.Contracts.Races;

public sealed record RaceEntryRepairInspectionDto(RaceRepairDetailsDto Race, int Version,
    IReadOnlyList<string> Blockers, IReadOnlyDictionary<string, int> ReferenceCounts,
    RaceEntryAssignmentsRepairedDto? PreviousRepair);
