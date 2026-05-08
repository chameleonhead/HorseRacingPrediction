namespace HorseRacingPrediction.Agents.JraAgent;

/// <summary>開催日程カレンダー上の 1 日分の情報。</summary>
public sealed record JraRaceScheduleDay(
    DateOnly Date,
    IReadOnlyList<string> Racecourses,
    string RawText);