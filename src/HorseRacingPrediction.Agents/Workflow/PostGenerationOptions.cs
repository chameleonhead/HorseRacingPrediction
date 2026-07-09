namespace HorseRacingPrediction.Agents.Workflow;

/// <summary>
/// <see cref="PostGenerationWorkflow"/> の実行パラメーターを保持するオプション。
/// appsettings.json の "PostGeneration" セクションから束縛される。
/// </summary>
public sealed class PostGenerationOptions
{
    public const string SectionName = "PostGeneration";

    /// <summary>投稿文生成ワークフローを実行するかどうか。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>投稿文の文字数上限。</summary>
    public int MaxCharacterCount { get; set; } = 400;

    /// <summary>投稿文の末尾に付与するハッシュタグ。</summary>
    public List<string> Hashtags { get; set; } = ["#競馬", "#JRA"];

    /// <summary>生成結果を保存する Memo の MemoType。</summary>
    public string MemoType { get; set; } = "SnsStoryPost";

    /// <summary>生成結果を保存する Memo の AuthorId。</summary>
    public string AuthorId { get; set; } = "PostGenerationWorkflow";
}
