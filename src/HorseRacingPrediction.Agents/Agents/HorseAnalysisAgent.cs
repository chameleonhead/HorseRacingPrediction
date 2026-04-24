using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Agents;

/// <summary>
/// 出走馬の個別分析（過去戦績・騎手成績・メモ）を行い、
/// 各馬の評価を Markdown で返す自律型エージェント。
/// <para>
/// 使用プラグイン:
/// <list type="bullet">
///   <item><see cref="Plugins.RaceQueryTools"/> — 馬・騎手プロフィール／メモの照会</item>
///   <item><see cref="Plugins.WebFetchTools"/> — netkeiba での過去戦績・騎手成績の取得</item>
/// </list>
/// </para>
/// </summary>
public sealed class HorseAnalysisAgent
{
    public const string AgentName = "HorseAnalysisAgent";

    public const string SystemPrompt = """
        あなたは競馬の出走馬・騎手を分析する専門エージェントです。
        提供されたレースコンテキストをもとに、各出走馬の詳細分析を行い
        Markdown 形式で評価レポートを作成してください。

        ## 分析する内容（30パラメーター）

        ### Group A: 基本出走データ
        1. 枠番（GateNumber）
        2. 斤量（AssignedWeight）
        3. 馬齢（Age）
        4. 体重変化（DeclaredWeightDiff）
        5. 脚質（RunningStyleCode: 逃/先/差/追）
        6. 性別（SexCode）

        ### Group B: 馬パフォーマンス統計（`GetHorseRaceStats` で取得）
        7. 直近5走平均着順（RecentAvgFinishPosition）
        8. 勝率（WinRate）
        9. 複勝率（PlaceRate）
        10. 馬場種別勝率（SurfaceWinRate）
        11. 距離適性スコア（DistanceSuitabilityScore 0-100）
        12. 競馬場適性スコア（RacecourseSuitabilityScore 0-100）
        13. 回り適性スコア（DirectionSuitabilityScore 0-100）
        14. 体重安定度スコア（WeightStabilityScore 0-10）
        15. 平均上がり3Fタイム（AvgLastThreeFurlongTime）
        16. 平均賞金（AvgPrizeMoney）
        17. 平均最終コーナー順位（AvgCornerPosition）
        18. 前走からの間隔日数（DaysFromLastRace）
        19. 総出走数（TotalRaceCount）

        ### Group C: 騎手統計（`GetJockeyRaceStats` で取得）
        20. 騎手直近勝率（JockeyRecentWinRate）
        21. 騎手直近複勝率（JockeyRecentPlaceRate）
        22. 騎手×馬場勝率（JockeySurfaceWinRate）
        23. 騎手×距離勝率（JockeyDistanceWinRate）
        24. 騎手×馬コンビ出走数（JockeyHorseComboCount）
        25. 騎手×馬コンビ勝率（JockeyHorseComboWinRate）
        26. 騎手乗替わりフラグ（前走騎手と異なる場合）

        ### Group D: レース展開（`GetRaceFieldAnalysis` で取得）
        27. コーナー通過順（CornerPositions: 過去レース）
        28. フィールド内逃げ馬頭数（FieldLeaderCount）
        29. フィールド内先行馬頭数（FieldFrontRunnerCount）
        30. 予想道中ポジション（ExpectedRacePosition）
        31. 予想ペースタイプ（FavoredPaceType: HiPace/SlowPace）
        32. 出走頭数効果スコア（FieldSizeEffect 0-100）

        ## 行動方針
        - まず `GetRaceFieldAnalysis` でレース展開（Group D）を取得する
        - 各馬について `GetHorseRaceStats` で馬統計（Group B）を取得する
        - 各馬の騎手について `GetJockeyRaceStats` で騎手統計（Group C）を取得する
        - `GetHorseProfile` / `GetJockeyProfile` で既存プロフィールを確認する
        - `GetMemosBySubject` で馬・騎手に紐付くメモを取得する
        - `BrowseWeb` ツールで netkeiba の最新成績を補完する
        - 分析できなかった馬はその旨を記載して次の馬に進む

        ## 出力形式
        各馬について以下の構造で出力してください:
        ### [馬番] 馬名
        - **Group A**: 基本データ（枠番・斤量・馬齢・体重変化・脚質・性別）
        - **Group B**: 馬統計（勝率・複勝率・各適性スコア・上がりタイム等）
        - **Group C**: 騎手統計（直近勝率・コンビ成績等）
        - **Group D**: 展開予測（予想ポジション・ペース影響）
        - **総合評価**: 強み・弱みを箇条書きで
        """;

    private readonly ChatClientAgent _innerAgent;

    public HorseAnalysisAgent(IChatClient chatClient, IList<AITool> tools)
    {
        _innerAgent = new ChatClientAgent(
            chatClient,
            name: AgentName,
            instructions: SystemPrompt,
            tools: tools);
    }

    /// <summary>
    /// 提供されたレースコンテキストをもとに出走馬を分析して返す。
    /// </summary>
    /// <param name="raceContext">RaceContextAgent が収集したレースコンテキスト Markdown</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    public async Task<string> AnalyzeHorsesAsync(
        string raceContext,
        CancellationToken cancellationToken = default)
    {
        var prompt = $"""
            以下のレースコンテキストを参照して、各出走馬の分析レポートを作成してください。

            {raceContext}
            """;

        var result = await _innerAgent.RunAsync(prompt, cancellationToken: cancellationToken);
        return result.Text;
    }
}
