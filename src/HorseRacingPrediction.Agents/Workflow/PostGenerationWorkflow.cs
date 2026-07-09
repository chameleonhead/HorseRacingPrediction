using HorseRacingPrediction.Agents.Agents;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Agents.Workflow;

/// <summary>
/// 確定済み予測票をもとに、SNS 投稿用のストーリー仕立てのテキストを生成するワークフロー。
/// Microsoft Agent Framework の Parallelization パターン（3エージェント並行草稿）→
/// 統合（<see cref="StoryPostComposerAgent"/>）の2段構成。
/// <list type="number">
///   <item><see cref="HonmeiCommentaryAgent"/> / <see cref="AnaCommentaryAgent"/> / <see cref="DataRationaleAgent"/> — 並行実行</item>
///   <item><see cref="StoryPostComposerAgent"/> — 3草稿を起承転結の物語1本に統合</item>
/// </list>
/// </summary>
public sealed class PostGenerationWorkflow
{
    private readonly HonmeiCommentaryAgent _honmeiAgent;
    private readonly AnaCommentaryAgent _anaAgent;
    private readonly DataRationaleAgent _dataRationaleAgent;
    private readonly StoryPostComposerAgent _composerAgent;
    private readonly PostGenerationOptions _options;

    public PostGenerationWorkflow(
        HonmeiCommentaryAgent honmeiAgent,
        AnaCommentaryAgent anaAgent,
        DataRationaleAgent dataRationaleAgent,
        StoryPostComposerAgent composerAgent,
        IOptions<PostGenerationOptions> options)
    {
        _honmeiAgent = honmeiAgent;
        _anaAgent = anaAgent;
        _dataRationaleAgent = dataRationaleAgent;
        _composerAgent = composerAgent;
        _options = options.Value;
    }

    /// <summary>
    /// 指定した予測票について、3エージェントの草稿を並行生成し、
    /// ストーリー仕立ての投稿文1本に統合して返す。
    /// </summary>
    /// <param name="predictionTicketId">確定済みの予測票 ID</param>
    /// <param name="raceId">対象レース ID</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    public async Task<PostGenerationWorkflowResult> RunAsync(
        string predictionTicketId,
        string raceId,
        CancellationToken cancellationToken = default)
    {
        var honmeiTask = _honmeiAgent.DraftAsync(predictionTicketId, cancellationToken);
        var anaTask = _anaAgent.DraftAsync(predictionTicketId, cancellationToken);
        var dataRationaleTask = _dataRationaleAgent.DraftAsync(raceId, predictionTicketId, cancellationToken);

        await Task.WhenAll(honmeiTask, anaTask, dataRationaleTask).ConfigureAwait(false);

        var postText = await _composerAgent.ComposeAsync(
            raceId,
            honmeiTask.Result,
            anaTask.Result,
            dataRationaleTask.Result,
            _options.MaxCharacterCount,
            _options.Hashtags,
            cancellationToken).ConfigureAwait(false);

        return new PostGenerationWorkflowResult(
            predictionTicketId, raceId, honmeiTask.Result, anaTask.Result, dataRationaleTask.Result, postText);
    }
}
