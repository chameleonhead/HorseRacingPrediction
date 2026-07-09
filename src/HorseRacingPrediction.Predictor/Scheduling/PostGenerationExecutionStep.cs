using HorseRacingPrediction.Agents.Workflow;
using HorseRacingPrediction.Collector.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Predictor.Scheduling;

/// <summary>
/// 予測票の確定に成功した直後に、ストーリー仕立ての SNS 投稿文生成を実行し
/// <see cref="IMemoWriteService"/> 経由で Memo として保存するステップ。
/// <para>
/// 予想自体は <see cref="ApiOnlyPredictionWorkflow"/> により LLM 不使用で確定済みのため、
/// このステップの失敗は予想の再キューに影響させず、ログ警告のみに留める（best-effort）。
/// </para>
/// </summary>
public sealed class PostGenerationExecutionStep
{
    private readonly PostGenerationOptions _options;
    private readonly PostGenerationWorkflow _workflow;
    private readonly IMemoWriteService _memoWriteService;
    private readonly ILogger<PostGenerationExecutionStep> _logger;

    public PostGenerationExecutionStep(
        IOptions<PostGenerationOptions> options,
        PostGenerationWorkflow workflow,
        IMemoWriteService memoWriteService,
        ILogger<PostGenerationExecutionStep> logger)
    {
        _options = options.Value;
        _workflow = workflow;
        _memoWriteService = memoWriteService;
        _logger = logger;
    }

    public async Task RunAsync(string predictionTicketId, string raceId, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        try
        {
            var result = await _workflow.RunAsync(predictionTicketId, raceId, cancellationToken).ConfigureAwait(false);
            var memoId = $"memo-post-{predictionTicketId}";

            await _memoWriteService.CreateOrUpdateRaceMemoAsync(
                raceId,
                _options.MemoType,
                result.PostText,
                _options.AuthorId,
                memoId,
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "[投稿文生成] 完了: RaceId={RaceId} TicketId={TicketId} MemoId={MemoId} Length={Length}",
                raceId,
                predictionTicketId,
                memoId,
                result.PostText.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[投稿文生成] 失敗しました（予想自体は確定済みのため処理を継続します）。RaceId={RaceId} TicketId={TicketId}",
                raceId,
                predictionTicketId);
        }
    }
}
