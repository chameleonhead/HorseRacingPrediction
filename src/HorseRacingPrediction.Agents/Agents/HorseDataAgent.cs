using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Agents;

/// <summary>
/// ウェブ検索エージェントを使って競走馬の詳細情報を収集する自律型エージェント。
/// <para>
/// 収集対象:
/// <list type="bullet">
///   <item>過去の出走成績（着順・タイム・着差）</item>
///   <item>血統情報（父・母・母父）</item>
///   <item>コース・距離・馬場適性</item>
///   <item>調教タイム・仕上がり状態</item>
///   <item>近走パターン・脚質傾向</item>
/// </list>
/// </para>
/// </summary>
public sealed class HorseDataAgent
{
    public const string AgentName = "HorseDataAgent";

    public const string SystemPrompt = """
        あなたは競走馬の情報を収集する専門エージェントです。
        指定された馬について、インターネット（netkeiba・JRA 公式など）から
        以下の情報を収集し、Markdown 形式で返してください。

        ## 収集する情報
        1. **基本情報**: 馬名・性別・馬齢・毛色・生年月日・馬主・生産者
        2. **血統**: 父・母・母父（サイアーライン）
        3. **過去成績**: 直近10走の着順・レース名・距離・馬場・タイム・着差・騎手・斤量
        4. **コース・距離適性**: 芝/ダート別、距離帯別の勝率・連対率
        5. **馬場状態適性**: 良・稍重・重・不良別の成績
        6. **調教情報**: 最新の調教タイム・コース・評価
        7. **近走特記事項**: 出遅れ・不利・馬体重増減などの注目点

        ## 行動方針
        - `BrowseWeb` ツールに取得したい情報を自然言語で依頼してインターネットから情報を取得する
          - netkeiba の馬戦績ページ
          - 「馬名 血統」「馬名 調教」などの検索
          - JRA 公式の馬情報ページ
          - 収集後は `UpsertHorse` を使ってドメインの馬プロフィールを更新する
        - 取得できなかった項目は「不明」と記載し、他の項目を続ける

        ## 出力形式
        Markdown 形式。見出しは ## と ### を使用し、成績は表形式で記載してください。
        """;

    private readonly ChatClientAgent _innerAgent;

    public HorseDataAgent(IChatClient chatClient, IList<AITool> tools)
    {
        _innerAgent = new ChatClientAgent(
            chatClient,
            name: AgentName,
            instructions: SystemPrompt,
            tools: tools);
    }

    /// <summary>
    /// 指定した馬の詳細情報を収集して返す。
    /// </summary>
    /// <param name="horseName">馬名（日本語可）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>収集した馬情報（Markdown 形式）</returns>
    public async Task<string> CollectAsync(
        string horseName,
        CancellationToken cancellationToken = default)
    {
        var result = await _innerAgent.RunAsync(
            $"競走馬「{horseName}」の詳細情報を収集してください。",
            cancellationToken: cancellationToken);

        return result.Text;
    }
}
