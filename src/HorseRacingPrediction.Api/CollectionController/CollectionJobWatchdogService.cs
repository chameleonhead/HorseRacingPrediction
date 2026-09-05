using HorseRacingPrediction.Collector.Scheduling;
using Microsoft.Extensions.Logging;

namespace HorseRacingPrediction.Api.CollectionController;

/// <summary>
/// 収集ジョブ（<see cref="AgentJobType.RaceCardCollection"/> / <see cref="AgentJobType.RaceResultCollection"/> 等）が
/// 詰まっていないかを定期的に確認し、必要なら自己修復する「ジョブコントローラー」側の監視。
/// <para>
/// 本番では Collector（Worker）は常駐 BackgroundService ではなく、SQS 通知をトリガーに
/// Lambda が <c>--once</c> で1回だけ起動する構成になっている。そのため、従来
/// <see cref="Scheduling.CollectionExecutionService"/> の <c>ExecuteAsync</c> 起動時にのみ
/// 行っていた「リース切れの Running ジョブを Ready へ戻す」処理や、Ready なのに
/// ディスパッチ通知（SQS向けoutbox）が無いジョブへの再ディスパッチが、Lambda運用では
/// 一切実行されない空白期間になっていた。本サービスはこれを Api（Job Controller）側で
/// 定期的に肩代わりする。
/// </para>
/// <para>
/// 注意: 本サービスはジョブの「取りこぼし」を再ディスパッチ・再キュー投入することで
/// 解消するものであり、Worker 側の実行そのものが恒常的に失敗している場合（例:
/// Collector の <c>--once</c> 実行経路自体が例外を送出する等）は、再ディスパッチしても
/// 実行が成功するようにはならない。その場合は Worker 側の実行経路の修正が別途必要。
/// </para>
/// </summary>
public sealed class CollectionJobWatchdogService : BackgroundService
{
    private static readonly string[] RecoverableJobTypes =
    [
        AgentJobType.RaceCardCollection,
        AgentJobType.RaceResultCollection
    ];

    private readonly ProcessingStateStore _store;
    private readonly CollectionMaintenanceState _maintenance;
    private readonly CollectionJobWatchdogOptions _options;
    private readonly ILogger<CollectionJobWatchdogService> _logger;

    public CollectionJobWatchdogService(
        ProcessingStateStore store,
        CollectionMaintenanceState maintenance,
        Microsoft.Extensions.Options.IOptions<CollectionJobWatchdogOptions> options,
        ILogger<CollectionJobWatchdogService> logger)
    {
        _store = store;
        _maintenance = maintenance;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("CollectionJobWatchdogService は無効化されています。");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "収集ジョブ監視サイクルでエラーが発生しました。");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes)),
                    stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (_maintenance.IsActive)
        {
            _logger.LogInformation("収集ジョブ監視: メンテナンス中のためスキップします。");
            return;
        }

        var now = DateTimeOffset.UtcNow;

        // リース期限が切れた Running ジョブ（Worker がクラッシュ・タイムアウトした等で
        // 完了/失敗報告のないまま放置されたもの）を Ready へ戻す。常駐 Worker であれば
        // 起動時に一度行われる処理だが、Lambda --once 運用ではこれを行うタイミングが
        // 存在しないため、ここで定期的に肩代わりする。
        var recoveredRunning = await _store
            .RequeueRunningJobsAsync(RecoverableJobTypes, now, cancellationToken)
            .ConfigureAwait(false);

        if (recoveredRunning > 0)
        {
            _logger.LogWarning(
                "収集ジョブ監視: リース切れの Running ジョブを Ready へ戻しました。Count={Count}",
                recoveredRunning);
        }

        // Ready 状態なのに SQS へのディスパッチ通知（outbox）が存在しないジョブへ
        // 再ディスパッチを行う。ディスパッチの取りこぼしや、Worker 側の実行失敗で
        // ジョブが Ready のまま長時間進行しないケースを拾う。
        var redispatched = await _store
            .RequeueReadyCollectionDispatchesAsync(now, cancellationToken)
            .ConfigureAwait(false);

        if (redispatched > 0)
        {
            _logger.LogWarning(
                "収集ジョブ監視: ディスパッチ通知の無い Ready ジョブを再ディスパッチしました。Count={Count}",
                redispatched);
        }

        if (recoveredRunning == 0 && redispatched == 0)
        {
            _logger.LogInformation("収集ジョブ監視: 詰まっているジョブは見つかりませんでした。");
        }
    }
}
