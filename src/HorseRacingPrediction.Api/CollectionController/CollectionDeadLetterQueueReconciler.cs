using System.Text.Json;
using HorseRacingPrediction.Api.Notifications;
using HorseRacingPrediction.Collector.Scheduling;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.CollectionController;

/// <summary>
/// SQSの再試行(maxReceiveCount)を無効化し1回の失敗で即DLQへ送るようにしたため
/// （infra/collector-lambda/main.tf）、DLQに残ったメッセージを拾い上げてジョブ状態へ
/// 反映する責務はこちら（API/ジョブコントローラー側）が持つ。
/// DLQからメッセージを回収し、対応するジョブをFailedへ確定させたうえでメッセージを削除する。
/// 一定回数（<see cref="CollectionDeadLetterQueueReconcilerOptions.ConsecutiveFailureThreshold"/>）
/// を超えたら、収集ジョブ全体を停止（メンテナンスモード開始・キューパージ）し、SNSへ通知する。
/// </summary>
public sealed class CollectionDeadLetterQueueReconciler : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ProcessingStateStore _store;
    private readonly ICollectionTaskQueue _queue;
    private readonly CollectionMaintenanceState _maintenance;
    private readonly ICollectionPipelineAlertPublisher _alertPublisher;
    private readonly CollectionDeadLetterQueueReconcilerOptions _options;
    private readonly ILogger<CollectionDeadLetterQueueReconciler> _logger;

    public CollectionDeadLetterQueueReconciler(
        ProcessingStateStore store,
        ICollectionTaskQueue queue,
        CollectionMaintenanceState maintenance,
        ICollectionPipelineAlertPublisher alertPublisher,
        IOptions<CollectionDeadLetterQueueReconcilerOptions> options,
        ILogger<CollectionDeadLetterQueueReconciler> logger)
    {
        _store = store;
        _queue = queue;
        _maintenance = maintenance;
        _alertPublisher = alertPublisher;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("CollectionDeadLetterQueueReconciler は無効化されています。");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DLQ回収サイクルでエラーが発生しました。");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.IntervalSeconds)), stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var messages = await _queue
            .ReceiveDeadLetterMessagesAsync(_options.MaxMessagesPerCycle, cancellationToken)
            .ConfigureAwait(false);

        if (messages.Count == 0) return;

        var failedCount = 0;
        foreach (var message in messages)
        {
            try
            {
                var notification = JsonSerializer.Deserialize<CollectionTaskNotification>(message.Body, JsonOptions);
                if (notification is not null)
                {
                    await _store.FailJobAsync(
                        notification.JobType,
                        notification.DeduplicationKey,
                        "Message moved to the collection dead-letter queue (Lambda execution failed).",
                        cancellationToken).ConfigureAwait(false);
                    _maintenance.RecordDlqFailure();
                    failedCount++;
                }
                else
                {
                    _logger.LogWarning("DLQメッセージの解析結果がnullでした。Body={Body}", message.Body);
                }
            }
            catch (Exception ex)
            {
                // 解析不能なメッセージをキューに残し続けると詰まり続けるため、失敗として
                // ジョブへ反映できなくても削除して回収したこと自体は進める。
                _logger.LogWarning(ex, "DLQメッセージの解析/反映に失敗しました。Body={Body}", message.Body);
            }
            finally
            {
                await _queue.DeleteDeadLetterMessageAsync(message.ReceiptHandle, cancellationToken).ConfigureAwait(false);
            }
        }

        var totalFailureCount = _maintenance.DlqFailureCount;
        _logger.LogError(
            "収集ジョブ監視: DLQから{Count}件のメッセージを回収しました（うちジョブへFailed反映={FailedCount}件）。累計DLQ失敗回数={Total}",
            messages.Count,
            failedCount,
            totalFailureCount);

        if (totalFailureCount < _options.ConsecutiveFailureThreshold) return;
        if (!_maintenance.TryBegin()) return; // 既に停止済み（二重トリップ防止）

        var reason = $"DLQ経由の失敗が{totalFailureCount}回に達したため（閾値={_options.ConsecutiveFailureThreshold}）、収集ジョブ全体を停止しました。";
        _logger.LogCritical("収集ジョブ監視: {Reason}", reason);

        try
        {
            await _queue.PurgeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "収集ジョブ停止時のキューパージに失敗しました。");
        }

        try
        {
            await _alertPublisher.PublishCollectionStoppedAsync(reason, totalFailureCount, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "収集ジョブ停止アラートのSNS送信に失敗しました。");
        }
    }
}
