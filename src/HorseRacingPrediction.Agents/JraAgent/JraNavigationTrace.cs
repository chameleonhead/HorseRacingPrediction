namespace HorseRacingPrediction.Agents.JraAgent;

/// <summary>
/// 目的ページに到達するまでに取った操作ステップと所要時間を記録する。
/// </summary>
public sealed record JraNavigationTrace(
    IReadOnlyList<string> Steps,
    TimeSpan Elapsed);