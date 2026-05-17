namespace HorseRacingPrediction.AgentClient.Scheduling;

public enum AgentJobStatus
{
    Pending = 0,
    Ready = 1,
    Running = 2,
    WaitingDependency = 3,
    Succeeded = 4,
    Failed = 5,
    Cancelled = 6,
    DeadLetter = 7
}