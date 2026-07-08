namespace HorseRacingPrediction.Collector.Http;

public interface IMemoWriteService
{
    Task<string?> CreateRaceMemoAsync(
        string raceId,
        string memoType,
        string content,
        string authorId,
        string? memoId,
        CancellationToken cancellationToken = default);
}
