namespace HorseRacingPrediction.Collector.Scheduling;

public sealed class CollectionExecutionTrigger
{
    private readonly SemaphoreSlim _signal = new(0);

    public void Signal()
    {
        if (_signal.CurrentCount == 0)
        {
            _signal.Release();
        }
    }

    public async Task<bool> WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        var delayTask = Task.Delay(delay, cancellationToken);
        var signalTask = _signal.WaitAsync(cancellationToken);
        var completedTask = await Task.WhenAny(signalTask, delayTask).ConfigureAwait(false);

        if (completedTask == signalTask)
        {
            await signalTask.ConfigureAwait(false);

            while (_signal.CurrentCount > 0 && _signal.Wait(0))
            {
            }

            return true;
        }

        await delayTask.ConfigureAwait(false);
        return false;
    }
}
