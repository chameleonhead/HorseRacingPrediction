namespace HorseRacingPrediction.Application.Queries.ReadModels;

public sealed record RaceEntryIndexSnapshot(
    string EntryId,
    string HorseId,
    int HorseNumber);