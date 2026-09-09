namespace HorseRacingPrediction.Collector.Scheduling;

public sealed partial class CollectionExecutionService
{
    private async Task ScheduleHorseOfficialInformationAsync(
        DateOnly raceDate,
        IReadOnlyList<string> raceIds,
        DateTimeOffset now,
        CancellationToken token)
    {
        if (!_options.EnableAutonomousHistoricalCollection) return;

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Jst).Date);
        if (raceDate < today || raceDate > today.AddDays(7)) return;

        var weekStart = raceDate.AddDays(-(((int)raceDate.DayOfWeek + 6) % 7));
        foreach (var raceId in raceIds)
        {
            var context = await _raceQueryService.GetRacePredictionContextAsync(raceId, token).ConfigureAwait(false);
            if (context is null) continue;

            foreach (var entry in context.Entries.DistinctBy(x => x.HorseId))
            {
                var horse = await _raceQueryService.GetHorseAsync(entry.HorseId, token).ConfigureAwait(false);
                if (horse is null || string.IsNullOrWhiteSpace(horse.RegisteredName)) continue;
                var subject = new SubjectCollectionPayload(horse.HorseId, "Horse", horse.RegisteredName, horse.BirthDate);

                await ScheduleOnceAsync(AgentJobType.SubjectProfileRefresh, subject, weekStart, 185, token).ConfigureAwait(false);
                await ScheduleOnceAsync(AgentJobType.HorseHistoryDiscovery, subject, weekStart, 184, token).ConfigureAwait(false);
            }
        }

        async Task ScheduleOnceAsync(string type, SubjectCollectionPayload subject, DateOnly week, int priority, CancellationToken cancellationToken)
        {
            var marker = $"{type}:{subject.SubjectId}:{week:yyyy-MM-dd}";
            if (await _stateStore.HasMarkerAsync("automatic-horse-information", marker, cancellationToken).ConfigureAwait(false)) return;
            var key = $"{subject.SubjectId}:auto:{week:yyyyMMdd}";
            await _stateStore.EnqueueJobAsync(type, key, AgentJobPayloadSerializer.Serialize(subject), now, priority, cancellationToken).ConfigureAwait(false);
            await _stateStore.MarkMarkerAsync("automatic-horse-information", marker, cancellationToken).ConfigureAwait(false);
        }
    }
}
