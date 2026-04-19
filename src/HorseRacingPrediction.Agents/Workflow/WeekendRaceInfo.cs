namespace HorseRacingPrediction.Agents.Workflow;

/// <summary>
/// <see cref="Agents.WeekendRaceDiscoveryAgent"/> が発見した週末レースの情報。
/// </summary>
public sealed record WeekendRaceInfo(
    /// <summary>レース名（例: 天皇賞秋）</summary>
    string RaceName,
    /// <summary>開催日</summary>
    DateOnly RaceDate,
    /// <summary>競馬場名（例: 東京）</summary>
    string Racecourse,
    /// <summary>レース番号（1〜12）</summary>
    int RaceNumber,
    /// <summary>エージェント検索用クエリ文字列（例: "2024年天皇賞秋 東京11R"）</summary>
    string RaceQuery,
    /// <summary>出走馬名一覧（発見時点で判明している分）</summary>
    IReadOnlyList<string> HorseNames,
    /// <summary>騎手名一覧（発見時点で判明している分）</summary>
    IReadOnlyList<string> JockeyNames,
    /// <summary>調教師名一覧（発見時点で判明している分）</summary>
    IReadOnlyList<string> TrainerNames);
