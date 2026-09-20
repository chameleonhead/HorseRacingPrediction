namespace HorseRacingPrediction.Contracts;

/// <summary>
/// 一括登録の結果。入力単位の採否は <see cref="Outcomes"/>、互換用の要約は
/// <see cref="Errors"/> に格納する。受理されたレース更新は一つの集約コミットとして適用する。
/// </summary>
public sealed record DeclareRaceResultBulkResponse(
    string RaceId,
    IReadOnlyList<string> Errors,
    IReadOnlyList<DeclareRaceResultBulkItemOutcome>? Outcomes = null,
    bool CorePersisted = false,
    IReadOnlyList<string>? RelatedErrors = null);

public sealed record DeclareRaceResultBulkItemOutcome(
    string Scope,
    string Key,
    string Status,
    string? ErrorCode = null,
    string? Message = null);
