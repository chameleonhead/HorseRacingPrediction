using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HorseRacingPrediction.Api.Tests;

/// <summary>
/// Test-only SQLite/EF instrumentation for the opt-in RC5 comparison. SQLite trace_v2 is
/// attached to the actual Microsoft.Data.Sqlite handle, so raw commands, TEMP statements,
/// transaction starts, and rows returned by SQLite are included as well as EF commands.
/// </summary>
internal sealed class CollectionDispatchRc5MeasurementInstrumentation : DbConnectionInterceptor,
    IMaterializationInterceptor, IDisposable
{
    private const uint SqliteTraceStatement = 0x01;
    private const uint SqliteTraceProfile = 0x02;
    private const uint SqliteTraceRow = 0x04;
    private const uint SqliteTraceEvents = SqliteTraceStatement | SqliteTraceRow | SqliteTraceProfile;
    private static readonly NativeTraceCallback NativeCallback = OnNativeTrace;
    private static readonly AsyncLocal<string?> Category = new();

    private readonly ConcurrentDictionary<string, MutableMetricBucket> _buckets = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string Category, string Sql), long> _sqlShapes = new();
    private readonly ConcurrentDictionary<(string Category, string Sql), MutableProfileBucket> _profileShapes = new();
    private readonly Dictionary<SqliteConnection, GCHandle> _registrations = new(ReferenceEqualityComparer.Instance);
    private readonly ConditionalWeakTable<DbContext, object> _measuredContexts = new();
    private readonly object _sync = new();
    private long _materializedEntities;
    private long _trackedEntities;
    private long _nativeObserverErrors;
    private int _measurementWindow;

    public CollectionDispatchRc5MeasurementInstrumentation(bool nativeSqliteTraceEnabled = true,
        bool nativeSqliteProfileEnabled = false)
    {
        NativeSqliteTraceEnabled = nativeSqliteTraceEnabled;
        NativeSqliteProfileEnabled = nativeSqliteTraceEnabled && nativeSqliteProfileEnabled;
    }

    public bool NativeSqliteTraceEnabled { get; }
    public bool NativeSqliteProfileEnabled { get; }
    public static bool NativeTraceEnabledFromEnvironment
        => !string.Equals(Environment.GetEnvironmentVariable("RC5_SQLITE_TRACE"), "off",
            StringComparison.OrdinalIgnoreCase);
    public static bool NativeProfileEnabledFromEnvironment
        => string.Equals(Environment.GetEnvironmentVariable("RC5_SQLITE_PROFILE"), "1",
            StringComparison.Ordinal);

    public string CurrentCategory => Category.Value ?? "operation";

    public IDisposable InCategory(string category)
    {
        var previous = Category.Value;
        Category.Value = category;
        return new CategoryScope(previous);
    }

    public IDisposable BeginMeasurementWindow()
    {
        Interlocked.Exchange(ref _measurementWindow, 1);
        return new MeasurementWindowScope(this);
    }

    public void Reset()
    {
        _buckets.Clear();
        _sqlShapes.Clear();
        _profileShapes.Clear();
        Interlocked.Exchange(ref _materializedEntities, 0);
        Interlocked.Exchange(ref _trackedEntities, 0);
        Interlocked.Exchange(ref _nativeObserverErrors, 0);
    }

    public CollectionDispatchRc5MetricsSnapshot Snapshot()
    {
        var categories = _buckets.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Snapshot(),
            StringComparer.Ordinal);
        var total = categories.Values.Aggregate(CollectionDispatchRc5MetricCounts.Empty,
            static (current, value) => current + value);
        var shapes = _sqlShapes.OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key.Category, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Sql, StringComparer.Ordinal)
            .Take(100)
            .Select(pair => new CollectionDispatchRc5SqlShape(pair.Key.Category, pair.Key.Sql, pair.Value))
            .ToArray();
        return new(total, categories, Interlocked.Read(ref _materializedEntities),
            Interlocked.Read(ref _trackedEntities), Interlocked.Read(ref _nativeObserverErrors), shapes,
            _profileShapes.Select(pair => new CollectionDispatchRc5SqlProfile(pair.Key.Category, pair.Key.Sql,
                    pair.Value.ExecutionCount, pair.Value.TotalElapsedNanoseconds, pair.Value.MaxElapsedNanoseconds))
                .OrderByDescending(shape => shape.TotalElapsedNanoseconds)
                .ThenBy(shape => shape.Category, StringComparer.Ordinal)
                .ThenBy(shape => shape.Sql, StringComparer.Ordinal).ToArray());
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (eventData.Context is { } context)
            AttachContext(context);
        Attach(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
            AttachContext(context);
        Attach(connection);
        return base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    public override InterceptionResult ConnectionClosing(DbConnection connection, ConnectionEventData eventData,
        InterceptionResult result)
    {
        Detach(connection);
        return base.ConnectionClosing(connection, eventData, result);
    }

    public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
    {
        Interlocked.Increment(ref _materializedEntities);
        AttachContext(materializationData.Context);
        return entity;
    }

    private void AttachContext(DbContext context)
    {
        lock (_sync)
        {
            if (_measuredContexts.TryGetValue(context, out _)) return;
            EventHandler<Microsoft.EntityFrameworkCore.ChangeTracking.EntityTrackedEventArgs> handler = (_, _) =>
            {
                if (Volatile.Read(ref _measurementWindow) != 0)
                    Interlocked.Increment(ref _trackedEntities);
            };
            _measuredContexts.Add(context, handler);
            context.ChangeTracker.Tracked += handler;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            foreach (var registration in _registrations.ToArray())
            {
                try { DetachHandle(registration.Key, registration.Value); }
                catch (ObjectDisposedException) { registration.Value.Free(); }
            }
            _registrations.Clear();
        }
    }

    private void Attach(DbConnection connection)
    {
        if (!NativeSqliteTraceEnabled) return;
        if (connection is not SqliteConnection sqlite) return;
        lock (_sync)
        {
            if (_registrations.ContainsKey(sqlite)) return;
            var registration = GCHandle.Alloc(this);
            var events = SqliteTraceStatement | SqliteTraceRow
                | (NativeSqliteProfileEnabled ? SqliteTraceProfile : 0);
            var result = SqliteTraceV2(sqlite.Handle!.DangerousGetHandle(), events,
                NativeCallback, GCHandle.ToIntPtr(registration));
            if (result != 0)
            {
                registration.Free();
                throw new InvalidOperationException($"sqlite3_trace_v2 failed with code {result}.");
            }
            _registrations.Add(sqlite, registration);
        }
    }

    private void Detach(DbConnection connection)
    {
        if (connection is not SqliteConnection sqlite) return;
        lock (_sync)
        {
            if (_registrations.Remove(sqlite, out var registration))
            {
                // SQLite removes trace callbacks when its native connection is closed. EF's
                // ConnectionClosing callback can arrive after SqliteConnection.Handle is null,
                // so only detach explicitly while the managed connection is still open.
                if (sqlite.State == System.Data.ConnectionState.Open && sqlite.Handle is { } handle)
                    DetachHandle(handle.DangerousGetHandle(), registration);
                else
                    registration.Free();
            }
        }
    }

    private static void DetachHandle(SqliteConnection connection, GCHandle registration)
    {
        if (connection.State == System.Data.ConnectionState.Open && connection.Handle is { } handle)
            DetachHandle(handle.DangerousGetHandle(), registration);
        else
            registration.Free();
    }

    private static void DetachHandle(IntPtr database, GCHandle registration)
    {
        var result = SqliteTraceV2(database, SqliteTraceEvents, null, IntPtr.Zero);
        registration.Free();
        if (result != 0)
            throw new InvalidOperationException($"sqlite3_trace_v2 detach failed with code {result}.");
    }

    private static int OnNativeTrace(uint eventCode, IntPtr context, IntPtr statement, IntPtr sql)
    {
        CollectionDispatchRc5MeasurementInstrumentation? owner = null;
        try
        {
            if (context == IntPtr.Zero) return 0;
            if (GCHandle.FromIntPtr(context).Target is not CollectionDispatchRc5MeasurementInstrumentation target)
                return 0;
            owner = target;

            if (eventCode == SqliteTraceStatement)
            {
                var text = sql == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(sql) ?? string.Empty;
                owner.RecordStatement(text);
            }
            else if (eventCode == SqliteTraceRow)
            {
                var nativeSql = NativeSqliteSql(statement);
                var text = nativeSql == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(nativeSql) ?? string.Empty;
                owner.Bucket().RecordRow(text);
            }
            else if (eventCode == SqliteTraceProfile && owner.NativeSqliteProfileEnabled)
            {
                var nativeSql = NativeSqliteSql(statement);
                var text = nativeSql == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(nativeSql) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(text) && sql != IntPtr.Zero)
                    owner.RecordProfile(text, Math.Max(0L, Marshal.ReadInt64(sql)));
            }
        }
        catch
        {
            // Never let a test observer cross the native SQLite callback boundary.
            if (owner is not null) Interlocked.Increment(ref owner._nativeObserverErrors);
        }
        return 0;
    }

    private void RecordStatement(string sql)
    {
        var bucket = Bucket();
        bucket.RecordStatement(sql);
        if (!string.IsNullOrWhiteSpace(sql))
        {
            var key = (CurrentCategory, Normalize(sql));
            _sqlShapes.AddOrUpdate(key, 1, static (_, count) => count + 1);
        }
    }

    private void RecordProfile(string sql, long elapsedNanoseconds)
    {
        var key = (CurrentCategory, Normalize(sql));
        _profileShapes.GetOrAdd(key, static _ => new()).Record(elapsedNanoseconds);
    }

    private MutableMetricBucket Bucket() => _buckets.GetOrAdd(CurrentCategory, static _ => new());

    private static string Normalize(string sql) => string.Join(' ', sql.Split((char[]?)null,
        StringSplitOptions.RemoveEmptyEntries));

    private sealed class CategoryScope(string? previous) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) Category.Value = previous;
        }
    }

    private sealed class MeasurementWindowScope(CollectionDispatchRc5MeasurementInstrumentation owner) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Interlocked.Exchange(ref owner._measurementWindow, 0);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeTraceCallback(uint eventCode, IntPtr context, IntPtr statement, IntPtr sql);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_trace_v2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SqliteTraceV2(IntPtr database, uint mask, NativeTraceCallback? callback, IntPtr context);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_sql", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr NativeSqliteSql(IntPtr statement);

    private sealed class MutableMetricBucket
    {
        private long _statements;
        private long _selects;
        private long _writes;
        private long _writerTransactions;
        private long _deferredTransactions;
        private long _rowsReturned;

        public void RecordStatement(string sql)
        {
            Interlocked.Increment(ref _statements);
            var keyword = LeadingKeyword(sql);
            if (keyword is "SELECT" or "WITH") Interlocked.Increment(ref _selects);
            if (keyword is "INSERT" or "UPDATE" or "DELETE" or "REPLACE")
                Interlocked.Increment(ref _writes);
            if (keyword == "BEGIN")
            {
                if (sql.Contains("IMMEDIATE", StringComparison.OrdinalIgnoreCase))
                    Interlocked.Increment(ref _writerTransactions);
                else
                    Interlocked.Increment(ref _deferredTransactions);
            }
        }

        public void RecordRow(string sql) => Interlocked.Increment(ref _rowsReturned);

        public CollectionDispatchRc5MetricCounts Snapshot() => new(
            Interlocked.Read(ref _statements), Interlocked.Read(ref _selects), Interlocked.Read(ref _writes),
            Interlocked.Read(ref _writerTransactions), Interlocked.Read(ref _deferredTransactions),
            Interlocked.Read(ref _rowsReturned));

        private static string LeadingKeyword(string sql)
        {
            var trimmed = sql.AsSpan().TrimStart();
            if (trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                var newline = trimmed.IndexOf('\n');
                if (newline >= 0) trimmed = trimmed[(newline + 1)..].TrimStart();
            }
            var end = 0;
            while (end < trimmed.Length && char.IsLetter(trimmed[end])) end++;
            return end == 0 ? string.Empty : trimmed[..end].ToString().ToUpperInvariant();
        }
    }

    private sealed class MutableProfileBucket
    {
        private long _executionCount;
        private long _totalElapsedNanoseconds;
        private long _maxElapsedNanoseconds;

        public long ExecutionCount => Interlocked.Read(ref _executionCount);
        public long TotalElapsedNanoseconds => Interlocked.Read(ref _totalElapsedNanoseconds);
        public long MaxElapsedNanoseconds => Interlocked.Read(ref _maxElapsedNanoseconds);

        public void Record(long elapsedNanoseconds)
        {
            Interlocked.Increment(ref _executionCount);
            Interlocked.Add(ref _totalElapsedNanoseconds, elapsedNanoseconds);
            var previous = Interlocked.Read(ref _maxElapsedNanoseconds);
            while (elapsedNanoseconds > previous)
            {
                var observed = Interlocked.CompareExchange(ref _maxElapsedNanoseconds, elapsedNanoseconds, previous);
                if (observed == previous) break;
                previous = observed;
            }
        }
    }
}

internal sealed record CollectionDispatchRc5MetricCounts(
    long SqlStatements,
    long SelectStatements,
    long WriteStatements,
    long ImmediateWriterTransactions,
    long DeferredTransactions,
    long SqliteRowsReturned)
{
    public static CollectionDispatchRc5MetricCounts Empty { get; } = new(0, 0, 0, 0, 0, 0);

    public static CollectionDispatchRc5MetricCounts operator +(
        CollectionDispatchRc5MetricCounts left, CollectionDispatchRc5MetricCounts right) => new(
        left.SqlStatements + right.SqlStatements,
        left.SelectStatements + right.SelectStatements,
        left.WriteStatements + right.WriteStatements,
        left.ImmediateWriterTransactions + right.ImmediateWriterTransactions,
        left.DeferredTransactions + right.DeferredTransactions,
        left.SqliteRowsReturned + right.SqliteRowsReturned);
}

internal sealed record CollectionDispatchRc5MetricsSnapshot(
    CollectionDispatchRc5MetricCounts Total,
    IReadOnlyDictionary<string, CollectionDispatchRc5MetricCounts> Categories,
    long EntityMaterializations,
    long TrackedEntities,
    long NativeObserverErrors,
    IReadOnlyList<CollectionDispatchRc5SqlShape> SqlShapes,
    IReadOnlyList<CollectionDispatchRc5SqlProfile> SqlProfiles);

internal sealed record CollectionDispatchRc5SqlShape(string Category, string Sql, long Count);
internal sealed record CollectionDispatchRc5SqlProfile(string Category, string Sql,
    long ExecutionCount, long TotalElapsedNanoseconds, long MaxElapsedNanoseconds);
