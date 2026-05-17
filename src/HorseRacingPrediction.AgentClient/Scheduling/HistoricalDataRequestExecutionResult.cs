namespace HorseRacingPrediction.AgentClient.Scheduling;

public sealed record HistoricalDataRequestExecutionResult(
    bool Succeeded,
    bool IsPermanentFailure,
    string? Message)
{
    public static HistoricalDataRequestExecutionResult Success(string? message = null)
        => new(true, false, message);

    public static HistoricalDataRequestExecutionResult Retry(string? message)
        => new(false, false, message);

    public static HistoricalDataRequestExecutionResult PermanentFailure(string? message)
        => new(false, true, message);
}