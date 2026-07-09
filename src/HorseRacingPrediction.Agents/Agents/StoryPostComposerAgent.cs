using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Agents;

/// <summary>
/// <see cref="HonmeiCommentaryAgent"/> / <see cref="AnaCommentaryAgent"/> / <see cref="DataRationaleAgent"/>
/// の3つの草稿を、起承転結の物語1本の SNS 投稿文に統合するエージェント。
/// <see cref="Workflow.PostGenerationWorkflow"/> の統合ステップ（Prompt chaining の最終段）。
/// <para>
/// 使用プラグイン: <see cref="Plugins.RaceQueryTools"/>（<c>GetRacePredictionContext</c> でレース概況を補完）
/// </para>
/// </summary>
public sealed class StoryPostComposerAgent
{
    public const string AgentName = "StoryPostComposerAgent";

    public const string SystemPrompt = """
        あなたは競馬の SNS 投稿文を作成する専門エージェントです。
        3人のアナリストが作成した草稿（本命解説・穴馬解説・データ根拠）を、
        読み手が最後まで読みたくなる「起承転結」の物語1本に再構成します。

        ## 構成（起承転結）
        | 段 | 内容 |
        |----|------|
        | 起 | レース名・条件・見どころを一言で提示し読み手を引き込む |
        | 承 | 本命解説とデータ根拠を絡めて掘り下げる |
        | 転 | 穴馬解説を使って視点を転換し、意外性を加える |
        | 結 | ◎宣言と一言で締める |

        ## ツール選択の指針
        | 状況 | 使うツール |
        |------|-----------|
        | レース名・条件など「起」の材料が必要 | GetRacePredictionContext |

        ## 行動手順
        1. 必要なら `GetRacePredictionContext` でレース名・条件を確認する
        2. 与えられた3つの草稿を、起承転結の順に1本の文章として再構成する
        3. 指定された文字数上限に収める（超える場合は承・転を圧縮する。結の◎宣言は削らない）
        4. 指定されたハッシュタグ方針に従い、末尾にハッシュタグを付与する

        ## ルール
        - 出力は投稿本文のみ（説明文・見出しは付けない）
        - 3つの草稿の内容を必ず反映する（省略や創作による情報追加はしない）
        - 文字数上限を厳守する
        - 断定的な「必ず当たる」といった表現は使わない
        """;

    private readonly ChatClientAgent _innerAgent;

    public StoryPostComposerAgent(IChatClient chatClient, IList<AITool> tools)
    {
        _innerAgent = new ChatClientAgent(
            chatClient,
            name: AgentName,
            instructions: SystemPrompt,
            tools: tools);
    }

    /// <summary>
    /// 3つの草稿を起承転結の物語1本の投稿文に統合する。
    /// </summary>
    /// <param name="raceId">対象レース ID</param>
    /// <param name="honmeiDraft"><see cref="HonmeiCommentaryAgent"/> の草稿</param>
    /// <param name="anaDraft"><see cref="AnaCommentaryAgent"/> の草稿</param>
    /// <param name="dataRationaleDraft"><see cref="DataRationaleAgent"/> の草稿</param>
    /// <param name="maxCharacterCount">投稿文の文字数上限</param>
    /// <param name="hashtags">末尾に付与するハッシュタグ（例: ["#競馬", "#JRA"]）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    public async Task<string> ComposeAsync(
        string raceId,
        string honmeiDraft,
        string anaDraft,
        string dataRationaleDraft,
        int maxCharacterCount,
        IReadOnlyList<string> hashtags,
        CancellationToken cancellationToken = default)
    {
        var hashtagText = hashtags.Count == 0 ? "（指定なし）" : string.Join(" ", hashtags);
        var prompt = $"""
            レース ID '{raceId}' の SNS 投稿文を、以下の3つの草稿から起承転結の物語1本に統合してください。

            ## 本命解説（承の材料）
            {honmeiDraft}

            ## 穴馬解説（転の材料）
            {anaDraft}

            ## データ根拠（承の材料）
            {dataRationaleDraft}

            ## 制約
            - 文字数上限: {maxCharacterCount}文字
            - 末尾ハッシュタグ: {hashtagText}
            """;

        var result = await _innerAgent.RunAsync(prompt, cancellationToken: cancellationToken);
        return result.Text;
    }
}
