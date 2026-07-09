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

    /// <summary>
    /// 決定論的な <paramref name="memoId"/> でレース紐付けメモの作成を試み、
    /// 既に存在する場合（409 Conflict）は内容を更新する。再生成を冪等に行うためのメソッド。
    /// </summary>
    Task<string> CreateOrUpdateRaceMemoAsync(
        string raceId,
        string memoType,
        string content,
        string authorId,
        string memoId,
        CancellationToken cancellationToken = default);
}
