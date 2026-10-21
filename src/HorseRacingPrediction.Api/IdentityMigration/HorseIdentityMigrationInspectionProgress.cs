using System.Diagnostics;
using HorseRacingPrediction.Contracts.Identity;

namespace HorseRacingPrediction.Api.IdentityMigration;

/// <summary>Request-local, page-boundary-only progress for the read-only preview path.</summary>
internal sealed class HorseIdentityMigrationInspectionProgress
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private HorseIdentityMigrationInspectionPhase _phase = HorseIdentityMigrationInspectionPhase.Request;
    private HorseIdentityMigrationInspectionResult _result = HorseIdentityMigrationInspectionResult.Running;
    private HorseIdentityMigrationSchemaSurface? _lastCompletedSurface;
    private string? _lastCompletedTable;
    private long _inspectedRowsLowerBound;
    private long _inspectedBytesLowerBound;

    internal void StartPhase(HorseIdentityMigrationInspectionPhase phase) => _phase = phase;

    internal void PageCompleted(MigrationStoreSurface surface, string table, long inspectedRows, long inspectedBytes)
    {
        _lastCompletedSurface = surface switch
        {
            MigrationStoreSurface.EventStore => HorseIdentityMigrationSchemaSurface.EventStore,
            MigrationStoreSurface.CollectionPlatform => HorseIdentityMigrationSchemaSurface.CollectionPlatform,
            MigrationStoreSurface.PredictionSchedule => HorseIdentityMigrationSchemaSurface.PredictionSchedule,
            MigrationStoreSurface.LocalCollectionQueue => HorseIdentityMigrationSchemaSurface.LocalCollectionQueue,
            _ => null,
        };
        _lastCompletedTable = TypedHorseIdentityReferenceReader.IsKnownDiagnosticPage(surface, table)
            ? table
            : null;
        _inspectedRowsLowerBound = Math.Max(_inspectedRowsLowerBound, inspectedRows);
        _inspectedBytesLowerBound = Math.Max(_inspectedBytesLowerBound, inspectedBytes);
    }

    internal void Finish(bool complete)
    {
        if (complete)
            _phase = HorseIdentityMigrationInspectionPhase.Complete;
        _result = complete
            ? HorseIdentityMigrationInspectionResult.Completed
            : HorseIdentityMigrationInspectionResult.Incomplete;
    }

    internal HorseIdentityMigrationInspectionDiagnostic Snapshot(
        HorseIdentityMigrationInspectionResult? resultOverride = null) => new(
        _phase,
        resultOverride ?? _result,
        _lastCompletedSurface,
        _lastCompletedTable,
        _inspectedRowsLowerBound,
        _inspectedBytesLowerBound,
        Math.Max(0, (long)_stopwatch.Elapsed.TotalMilliseconds));
}
