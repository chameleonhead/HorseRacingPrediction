using System.Text;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;

namespace HorseRacingPrediction.Agents.Agents;

/// <summary>
/// 指定されたレースの予測コンテキスト（出馬表・馬場・天候）を収集し、
/// 構造化 Markdown で返す自律型エージェント。
/// <para>
/// 使用プラグイン:
/// <list type="bullet">
///   <item><see cref="Plugins.RaceQueryTools"/> — 既存 EventFlow ReadModel の照会</item>
///   <item><see cref="Plugins.WebFetchTools"/> — Playwright による最新出馬表の取得（省略可）</item>
/// </list>
/// </para>
/// </summary>
public sealed class RaceContextAgent
{
    private const string AgentName = "RaceContextAgent";

    private const string SystemPrompt = """
        あなたは競馬レースの予測コンテキスト収集エージェントです。
        指定されたレース ID に対して、以下の情報を収集・整理して Markdown 形式で返してください。

        ## 収集する情報
        1. **基本情報**: レース名・開催日・競馬場・距離・馬場種別・グレード
        2. **出走馬一覧**: 馬番・枠番・馬名・騎手名・斤量・性齢・申告体重
        3. **最新天候**: 天気・気温・湿度・風向・風速
        4. **最新馬場状態**: 芝・ダートの馬場状態

        ## 行動方針
        - まず `GetRacePredictionContext` ツールで既存データを取得する
        - 出走馬の馬 ID・騎手 ID が取得できた場合は `GetHorseProfile`・`GetJockeyProfile` で名前を補完する
        - 最新の出馬表情報が必要な場合は `FetchRaceCard` で補完する
        - 取得できた情報だけを整理して返す（情報が不足していてもエラーにしない）

        ## 出力形式
        必ず Markdown 形式で返してください。情報が取得できなかった項目は「不明」と記載してください。
        """;

    private readonly ChatCompletionAgent _innerAgent;

    public RaceContextAgent(Kernel kernel)
    {
        _innerAgent = new ChatCompletionAgent
        {
            Name = AgentName,
            Instructions = SystemPrompt,
            Kernel = kernel
        };
    }

    /// <summary>
    /// 指定したレース ID の予測コンテキストを収集して返す。
    /// </summary>
    public async Task<string> CollectContextAsync(
        string raceId,
        CancellationToken cancellationToken = default)
    {
        var prompt = $"レース ID '{raceId}' の予測コンテキストを収集してください。";
        var sb = new StringBuilder();

        await foreach (var response in _innerAgent.InvokeAsync(
            prompt,
            thread: null,
            options: null,
            cancellationToken: cancellationToken))
        {
            sb.Append(response.Message.Content);
        }

        return sb.ToString();
    }
}
