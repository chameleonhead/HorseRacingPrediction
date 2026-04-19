using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Agents;

/// <summary>
/// ウェブ検索エージェントを使って厩舎（調教師）の詳細情報を収集する自律型エージェント。
/// <para>
/// 収集対象:
/// <list type="bullet">
///   <item>調教師のプロフィール・所属厩舎情報</item>
///   <item>今年・直近の厩舎成績</item>
///   <item>管理馬の特徴・傾向</item>
///   <item>レース間隔・仕上がりパターン</item>
///   <item>得意条件（競馬場・距離・馬場）</item>
/// </list>
/// </para>
/// </summary>
public sealed class StableDataAgent
{
    public const string AgentName = "StableDataAgent";

    public const string SystemPrompt = """
        あなたは厩舎（調教師）の情報を収集する専門エージェントです。
        指定された調教師・厩舎について、インターネット（netkeiba・JRA 公式など）から
        以下の情報を収集し、Markdown 形式で返してください。

        ## 収集する情報
        1. **基本プロフィール**: 調教師名・所属・拠点（美浦/栗東）・デビュー年
        2. **通算成績**: 管理頭数・勝利数・重賞勝利数・通算勝率・連対率
        3. **今年の成績**: 年間出走数・勝利数・勝率・重賞成績
        4. **直近30日の成績**: 出走数・勝利数・勝率・主な管理馬
        5. **得意競馬場**: 競馬場別の勝率・出走数
        6. **得意距離帯・馬場**: 短距離〜長距離別、芝・ダート別の成績
        7. **主要管理馬**: 重賞馬・注目馬の一覧
        8. **仕上がりパターン**: 中間の調教傾向・休養明け成績・連闘成績

        ## 行動方針
        - `BrowseWeb` ツールに取得したい情報を自然言語で依頼してインターネットから情報を取得する
          - netkeiba の調教師成績ページ
          - 「調教師名 成績」「厩舎名 管理馬」などの検索
          - JRA 公式の調教師情報ページ
        - 取得できなかった項目は「不明」と記載し、他の項目を続ける

        ## 出力形式
        Markdown 形式。見出しは ## と ### を使用し、成績は表形式で記載してください。
        """;

    private readonly ChatClientAgent _innerAgent;

    public StableDataAgent(IChatClient chatClient, IList<AITool> tools)
    {
        _innerAgent = new ChatClientAgent(
            chatClient,
            name: AgentName,
            instructions: SystemPrompt,
            tools: tools);
    }

    /// <summary>
    /// 指定した調教師・厩舎の詳細情報を収集して返す。
    /// </summary>
    /// <param name="trainerName">調教師名（日本語可）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>収集した厩舎情報（Markdown 形式）</returns>
    public async Task<string> CollectAsync(
        string trainerName,
        CancellationToken cancellationToken = default)
    {
        var result = await _innerAgent.RunAsync(
            $"調教師・厩舎「{trainerName}」の詳細情報を収集してください。",
            cancellationToken: cancellationToken);

        return result.Text;
    }
}
