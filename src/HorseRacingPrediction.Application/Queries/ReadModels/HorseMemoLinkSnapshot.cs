namespace HorseRacingPrediction.Application.Queries.ReadModels;

public sealed record HorseMemoLinkSnapshot(
    string LinkId,
    string LinkType,
    string Title,
    string? Url,
    string? StorageKey);
