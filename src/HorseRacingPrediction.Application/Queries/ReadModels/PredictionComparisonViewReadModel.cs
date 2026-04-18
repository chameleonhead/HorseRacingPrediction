namespace HorseRacingPrediction.Application.Queries.ReadModels;

public sealed record PredictionComparisonViewReadModel(
    string RaceId,
    string RaceName,
    IReadOnlyList<PredictionTicketSnapshot> PredictionTickets,
    IReadOnlyList<EntryResultSnapshot> EntryResults);
