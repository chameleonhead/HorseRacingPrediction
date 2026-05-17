using HorseRacingPrediction.Agents.Workflow;

namespace HorseRacingPrediction.AgentClient.Scheduling;

public static class AgentWorkModeResolver
{
    public static AgentWorkMode Resolve(
        DateOnly today,
        JraRaceScheduleCollectionResult? schedule,
        int preRaceLeadDays)
    {
        if (schedule is null || !string.IsNullOrWhiteSpace(schedule.Error))
        {
            return AgentWorkMode.Idle;
        }

        if (schedule.RaceDates.Contains(today))
        {
            return AgentWorkMode.Live;
        }

        var windowEnd = today.AddDays(Math.Max(0, preRaceLeadDays));
        if (schedule.UpcomingRaceDates.Any(x => x >= today && x <= windowEnd))
        {
            return AgentWorkMode.PreRace;
        }

        return AgentWorkMode.Idle;
    }
}