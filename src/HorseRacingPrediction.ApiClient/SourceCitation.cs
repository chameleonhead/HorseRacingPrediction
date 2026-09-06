namespace HorseRacingPrediction.ApiClient;

/// <summary>
/// 引用元（データの取得元URL）を紐付ける対象。<c>SubjectType</c>は
/// メモ機能（<c>MemoSubjectType</c>: Horse/Trainer/Jockey/Race）と同じ値を使う。
/// </summary>
public sealed record CitationSubject(string SubjectType, string SubjectId);
