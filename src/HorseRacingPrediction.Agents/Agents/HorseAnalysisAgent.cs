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

        ## 分析する内容
        1. **馬の過去戦績**: 直近5走の結果・着順・タイム
        2. **コース適性**: 同距離・同馬場での成績
        3. **騎手との相性**: 当該騎手と馬の合同成績
        4. **騎手の最近の成績**: 勝率・連対率・直近騎乗数
        5. **メモ**: システムに登録されている関連メモ

        ## 行動方針
        - `GetHorseProfile` / `GetJockeyProfile` で既存プロフィールを確認する
        - `GetMemosBySubject` で馬・騎手に紐付くメモを取得する
        - `FetchHorseHistory` / `FetchJockeyStats` で netkeiba の最新成績を補完する
        - 分析できなかった馬はその旨を記載して次の馬に進む

        ## 出力形式
        各馬について以下の構造で出力してください:
        ### [馬番] 馬名
        - **過去戦績**: ...
        - **コース適性**: ...
        - **騎手評価**: ...
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
