using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Agents;

/// <summary>
/// ウェブ検索エージェントを使ってレースの詳細情報を収集する自律型エージェント。
/// <para>
/// 収集対象:
/// <list type="bullet">
///   <item>レースの基本条件（距離・馬場・格付け・賞金）</item>
///   <item>出馬表（出走馬・騎手・斤量・馬体重）</item>
///   <item>過去の同レース結果・傾向</item>
///   <item>当日の馬場状態・天候</item>
///   <item>オッズ・予想情報</item>
/// </list>
/// </para>
/// </summary>
public sealed class RaceDataAgent
{
    public const string AgentName = "RaceDataAgent";

    public const string SystemPrompt = """
        あなたはレース情報を収集する専門エージェントです。
        指定されたレースについて、インターネット（JRA 公式・netkeiba など）から
        以下の情報を収集し、Markdown 形式で返してください。

        ## 収集する情報
        1. **レース基本情報**: レース名・グレード・開催日・競馬場・コース・距離・馬場種別・賞金
        2. **出馬表**: 馬番・枠番・馬名・騎手・斤量・性齢・馬体重・調教師・馬主
        3. **馬場・天候**: 当日の天気・気温・湿度・芝/ダート馬場状態
        4. **過去の同レース傾向**（直近3〜5年）:
           - 人気別の勝率・連対率
           - 枠番・脚質別の傾向
           - 前走条件・ローテーションの傾向
        5. **レースペース想定**: 逃げ・先行馬の存在、ハイペース/スローペース予測
        6. **注目馬・事前情報**: 調教評価・陣営コメント・除外・取消情報

        ## 行動方針
        - `BrowseWeb` ツールに取得したい情報を自然言語で依頼してインターネットから情報を取得する
          - JRA 公式（www.jra.go.jp）の出馬表ページ
          - 過去の同レース結果
          - 「レース名 傾向」「レース名 過去結果」などの検索
          - netkeiba の詳細ページ
        - 取得できなかった項目は「不明」と記載し、他の項目を続ける

        ## 出力形式
        Markdown 形式。見出しは ## と ### を使用し、出馬表は表形式で記載してください。
        """;

    private readonly ChatClientAgent _innerAgent;

    public RaceDataAgent(IChatClient chatClient, IList<AITool> tools)
    {
        _innerAgent = new ChatClientAgent(
            chatClient,
            name: AgentName,
            instructions: SystemPrompt,
            tools: tools);
    }

    /// <summary>
    /// 指定したレースの詳細情報を収集して返す。
    /// </summary>
    /// <param name="raceQuery">
    /// レースを特定するクエリ文字列（例: "2024年天皇賞秋" や "2024/10/27 東京11R"）
    /// </param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>収集したレース情報（Markdown 形式）</returns>
    public async Task<string> CollectAsync(
        string raceQuery,
        CancellationToken cancellationToken = default)
    {
        var result = await _innerAgent.RunAsync(
            $"レース「{raceQuery}」の詳細情報を収集してください。",
            cancellationToken: cancellationToken);

        return result.Text;
    }
}
