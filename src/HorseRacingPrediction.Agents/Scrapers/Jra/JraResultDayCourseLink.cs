namespace HorseRacingPrediction.Agents.Scrapers.Jra;

/// <summary>
/// JRA 成績トップページから抽出した「開催日+競馬場」単位の結果ページへのリンク。
/// URL パターンを構築するのではなく、ページ上のリンクをそのまま保持する。
/// </summary>
public sealed record JraResultDayCourseLink(
    /// <summary>結果一覧ページの URL</summary>
    string Url,
    /// <summary>リンクの表示テキスト（例: "5月4日(日) 東京"）</summary>
    string Label,
    /// <summary>競馬場名（日本語、例: 東京）。ラベルから抽出できた場合のみ設定される</summary>
    string? Racecourse,
    /// <summary>開催日。ラベルから抽出できた場合のみ設定される</summary>
    DateOnly? RaceDate);
