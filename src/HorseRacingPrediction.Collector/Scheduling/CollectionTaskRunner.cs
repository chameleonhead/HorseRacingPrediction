using HorseRacingPrediction.Scraping.Jra.Workflow;

namespace HorseRacingPrediction.Collector.Scheduling;

/// <summary>Shares lease cancellation and timeout handling across local and Lambda workers.</summary>
public static class CollectionTaskRunner
{
    public static async Task RunAsync(IProcessingStateStore store, LeasedCollectionTask task,
        Func<CancellationToken, Task> execute, TimeSpan timeout, bool internalDeadline,
        string? requestId, CancellationToken cancellationToken)
    {
        using var work = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        work.CancelAfter(timeout);
        using var monitorStop = new CancellationTokenSource();
        var control = CollectionLeaseControl.Continue;
        Exception? monitorError = null;
        async Task MonitorAsync()
        {
            try
            {
                while (!monitorStop.IsCancellationRequested)
                {
                    using var poll = CancellationTokenSource.CreateLinkedTokenSource(monitorStop.Token);
                    poll.CancelAfter(TimeSpan.FromSeconds(3));
                    control = await store.GetCollectionLeaseControlAsync(task.TaskId, task.LeaseToken, poll.Token);
                    if (control != CollectionLeaseControl.Continue) { await work.CancelAsync(); return; }
                    await Task.Delay(TimeSpan.FromSeconds(1), monitorStop.Token);
                }
            }
            catch (OperationCanceledException) when (monitorStop.IsCancellationRequested) { }
            catch (Exception ex) { monitorError = ex; await work.CancelAsync(); }
        }

        var monitor = Task.CompletedTask;
        try
        {
            // Check an already-held or invalidated lease before starting browser work.
            using (var initialCheck = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                control = await store.GetCollectionLeaseControlAsync(task.TaskId, task.LeaseToken, initialCheck.Token);
            if (control != CollectionLeaseControl.Continue) return;
            monitor = MonitorAsync();
            work.Token.ThrowIfCancellationRequested();
            await execute(work.Token);
        }
        catch (Exception ex)
        {
            using var report = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            // The DB decision wins over a concurrently expiring timeout or completing work.
            control = await store.GetCollectionLeaseControlAsync(task.TaskId, task.LeaseToken, report.Token);
            if (control != CollectionLeaseControl.Continue) return;
            var failure = monitorError ?? ex;
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested && !internalDeadline)
            {
                await store.RequeueCollectionTaskAsync(task.JobType, task.DeduplicationKey, task.LeaseToken,
                    DateTimeOffset.UtcNow, "Workerの終了により中断しました。", report.Token);
                throw;
            }
            var timedOut = failure is TimeoutException || failure.GetBaseException() is TimeoutException
                || (failure is OperationCanceledException && work.IsCancellationRequested);
            var reason = timedOut
                ? (cancellationToken.IsCancellationRequested && internalDeadline
                    ? "Collector execution timed out (14-minute internal deadline reached)."
                    : $"Collector job timed out (limit: {timeout.TotalMinutes:g} minutes).")
                : failure.Message;
            if (!string.IsNullOrWhiteSpace(requestId)) reason += $" RequestId={requestId}";
            if (timedOut || ApiFailureClassifier.IsFatalServerError(failure))
            {
                var changed = await store.FailAndPauseCollectionTaskAsync(task.JobType, task.DeduplicationKey, task.LeaseToken, reason, report.Token);
                if (changed && timedOut) throw new TimeoutException(reason, ex);
            }
            else
                await store.FailCollectionTaskAsync(task.JobType, task.DeduplicationKey, task.LeaseToken, reason, report.Token);
        }
        finally
        {
            await monitorStop.CancelAsync();
            await monitor;
            // execute has unwound and disposed the browser before the hold is acknowledged.
            using var report = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await store.AcknowledgeCollectionHoldAsync(task.TaskId, task.LeaseToken, report.Token);
        }
    }
}
