using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Races;

public partial class RaceAggregate : IEmit<RaceEntryAssignmentsRepaired>
{
    public void RepairEntryAssignments(int expectedVersion, string operationId, string fingerprint,
        string sourceUrl, DateTimeOffset observedAt, IReadOnlyList<EntryDetails> entries, string? gradeCode, string sourceEvidenceJson)
    {
        if (_state.EntryRepairOperationId == operationId)
        {
            if (_state.EntryRepairFingerprint != fingerprint)
                throw new InvalidOperationException("Repair operation ID was reused with different data.");
            return;
        }
        if (!_state.IsCreated || Version != expectedVersion)
            throw new InvalidOperationException("Race version changed; preview again.");
        if (_state.EntryRepairOperationId is not null)
            throw new InvalidOperationException("This race already has an assignment repair.");
        if (_state.EntryResults.Count > 0 || _state.ResultDeclaredAt is not null
            || _state.PayoutResult is not null || _state.HasOddsSnapshots)
            throw new InvalidOperationException("Independent race references prevent assignment repair.");
        if (string.IsNullOrWhiteSpace(operationId) || string.IsNullOrWhiteSpace(fingerprint)
            || !Uri.TryCreate(sourceUrl, UriKind.Absolute, out var source)
            || source.Scheme != "https" || source.Host is not ("www.jra.go.jp" or "www.jra.jp")
            || source.AbsolutePath != "/JRADB/accessD.html" || observedAt == default || string.IsNullOrWhiteSpace(sourceEvidenceJson))
            throw new ArgumentException("An identified official card and repair operation are required.");
        if (entries.Count == 0 || entries.Count != _state.Entries.Count
            || entries.Any(x => x.HorseNumber is null or < 1 or > 18
                || x.EntryId != $"{Id.Value}-entry-{x.HorseId}" || x.GateNumber is < 1 or > 8
                || x.GateNumber is null || string.IsNullOrWhiteSpace(x.OwnerName))
            || entries.Select(x => x.HorseNumber).Distinct().Count() != entries.Count
            || entries.Select(x => x.HorseId).Distinct().Count() != entries.Count
            || !entries.Select(x => x.HorseId).Order().SequenceEqual(_state.Entries.Select(x => x.HorseId).Order()))
            throw new ArgumentException("Repair requires the complete, confirmed one-to-one horse set.");
        Emit(new RaceEntryAssignmentsRepaired(operationId, fingerprint, sourceUrl, observedAt, sourceEvidenceJson,
            _state.Entries.ToArray(), entries.ToArray(), _state.RaceDate!.Value,
            _state.RacecourseCode!, gradeCode, _state.SurfaceCode, _state.DistanceMeters, _state.DirectionCode));
    }

    public void Apply(RaceEntryAssignmentsRepaired e) { }
}
