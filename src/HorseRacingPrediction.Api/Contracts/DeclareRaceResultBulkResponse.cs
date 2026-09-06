namespace HorseRacingPrediction.Api.Contracts;

/// <summary>
/// 一括登録の結果。個々の項目（結果宣言・各馬の成績・天候・馬場状態・払戻）は
/// 1件失敗しても他の項目の登録は継続するため、失敗した項目は例外にせず
/// <see cref="Errors"/> に文言として集約して返す。
/// </summary>
public sealed record DeclareRaceResultBulkResponse(
    string RaceId,
    IReadOnlyList<string> Errors);
