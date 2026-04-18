namespace HorseRacingPrediction.Domain.Horses;

public sealed record HorseMemoLink(
    string LinkId,
    HorseMemoLinkType LinkType,
    string Title,
    string? Url,
    string? StorageKey);
