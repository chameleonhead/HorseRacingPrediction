using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Agents;

/// <summary>
/// ウェブ検索エージェントを使って騎手の詳細情報を収集する自律型エージェント。
/// <para>
/// 収集対象:
/// <list type="bullet">
///   <item>騎手の基本プロフィール（所属・年齢・デビュー年）</item>
///   <item>今年・直近の成績（勝率・連対率・複勝率）</item>
///   <item>コース・競馬場別の得意傾向</item>
///   <item>特定の厩舎・馬主との相性</item>
///   <item>芝・ダート別の成績傾向</item>
/// </list>
/// </para>
/// </summary>
public sealed class JockeyDataAgent
{
    public const string AgentName = "JockeyDataAgent";

    public const string SystemPrompt = """
        あなたは騎手（ジョッキー）の情報を収集する専門エージェントです。
        指定された騎手について、インターネット（netkeiba・JRA 公式など）から
        以下の情報を収集し、Markdown 形式で返してください。

        ## 収集する情報
        1. **基本プロフィール**: 騎手名・読み・所属・生年月日・デビュー年・代理人
        2. **通算成績**: 騎乗回数・1着〜3着回数・勝率・連対率・複勝率
        3. **今年の成績**: 年間騎乗数・勝利数・勝率・重賞勝利
        4. **直近30日の成績**: 騎乗数・勝利数・勝率・主な騎乗馬
        5. **競馬場別成績**: 競馬場ごとの勝率・得意競馬場
        6. **距離帯別成績**: 短距離・マイル・中距離・長距離別の勝率
        7. **馬場状態別成績**: 良・稍重・重・不良別の勝率
        8. **近走乗り替わり・継続騎乗**: 注目すべき乗り替わりや継続騎乗情報

        ## 行動方針
        - `FetchJockeyStats` で netkeiba の騎手成績ページを取得する
        - `SearchAndFetch` で「騎手名 成績」「騎手名 競馬場」などを検索して補完する
        - `FetchPageContent` で JRA 公式の騎手情報ページを取得する（必要な場合）
        - 取得できなかった項目は「不明」と記載し、他の項目を続ける

        ## 出力形式
        Markdown 形式。見出しは ## と ### を使用し、成績は表形式で記載してください。
        """;

    private readonly ChatClientAgent _innerAgent;

    public JockeyDataAgent(IChatClient chatClient, IList<AITool> tools)
    {
        _innerAgent = new ChatClientAgent(
            chatClient,
            name: AgentName,
            instructions: SystemPrompt,
            tools: tools);
    }

    /// <summary>
    /// 指定した騎手の詳細情報を収集して返す。
    /// </summary>
    /// <param name="jockeyName">騎手名（日本語可）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>収集した騎手情報（Markdown 形式）</returns>
    public async Task<string> CollectAsync(
        string jockeyName,
        CancellationToken cancellationToken = default)
    {
        var result = await _innerAgent.RunAsync(
            $"騎手「{jockeyName}」の詳細情報を収集してください。",
            cancellationToken: cancellationToken);

        return result.Text;
    }
}
