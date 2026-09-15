using System.Threading;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Scraping.Jra;
using Microsoft.Extensions.Logging;

namespace HorseRacingPrediction.Collector.CollectionPlatform;

/// <summary>
/// Shares one JRA browser session for the duration of a compatible collection envelope.
/// AsyncLocal keeps concurrent Lambda/local-queue executions isolated from one another.
/// </summary>
public static class JraSessionExecutionScope
{
    private static readonly AsyncLocal<ScopeState?> Current = new();

    public static async Task ExecuteAsync(IJraSessionFactory factory, Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken, CollectionDispatchCompatibilityKey? compatibilityKey = null,
        int taskCount = 0, ILogger? logger = null)
        => await ExecuteMeasuredAsync(factory, operation, cancellationToken, compatibilityKey, taskCount, logger,
            SystemCollectionTaskTelemetryClock.Instance).ConfigureAwait(false);

    internal static async Task ExecuteMeasuredAsync(IJraSessionFactory factory,
        Func<CancellationToken, Task> operation, CancellationToken cancellationToken,
        CollectionDispatchCompatibilityKey? compatibilityKey, int taskCount, ILogger? logger,
        ICollectionTaskTelemetryClock clock)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(operation);

        if (Current.Value is not null)
        {
            await operation(cancellationToken).ConfigureAwait(false);
            return;
        }

        var started = clock.GetTimestamp();
        using var apiTiming = CollectionRuntimeTimingContext.BeginSession();
        var result = "Succeeded";
        var state = new ScopeState(factory, compatibilityKey);
        Current.Value = state;
        try
        {
            await operation(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            result = "Cancelled";
            throw;
        }
        catch
        {
            result = "Failed";
            throw;
        }
        finally
        {
            Current.Value = null;
            try
            {
                await state.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                result = "Failed";
                throw;
            }
            finally
            {
                var elapsed = clock.GetElapsedTime(started, clock.GetTimestamp());
                var api = apiTiming.Accumulator.Snapshot();
                var nonApi = elapsed > api.ApiElapsed ? elapsed - api.ApiElapsed : TimeSpan.Zero;
                logger?.LogInformation(
                    "JRA session runtime. GroupKind={GroupKind} Result={Result} TaskCount={TaskCount} BrowserCreated={BrowserCreated} SessionMs={SessionMs} SessionApiMs={SessionApiMs} SessionApiCallCount={SessionApiCallCount} SessionNonApiMs={SessionNonApiMs}",
                    compatibilityKey?.GroupKind.ToString() ?? "Unspecified", result, Math.Max(0, taskCount),
                    state.BrowserCreated, elapsed.TotalMilliseconds, api.ApiElapsed.TotalMilliseconds,
                    api.ApiCallCount, nonApi.TotalMilliseconds);
            }
        }
    }

    public static async Task<JraSessionLease> AcquireAsync(IJraSessionFactory factory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var state = Current.Value;
        if (state is not null && ReferenceEquals(state.Factory, factory))
            return new JraSessionLease(await state.GetSessionAsync(cancellationToken).ConfigureAwait(false),
                ownsSession: false);

        return new JraSessionLease(await factory.CreateAsync(cancellationToken).ConfigureAwait(false),
            ownsSession: true);
    }

    public static CollectionDispatchCompatibilityKey? CurrentCompatibilityKey => Current.Value?.CompatibilityKey;

    private sealed class ScopeState(IJraSessionFactory factory,
        CollectionDispatchCompatibilityKey? compatibilityKey) : IAsyncDisposable
    {
        private readonly object _gate = new();
        private Task<JraSession>? _session;

        public IJraSessionFactory Factory { get; } = factory;
        public CollectionDispatchCompatibilityKey? CompatibilityKey { get; } = compatibilityKey;
        public bool BrowserCreated
        {
            get { lock (_gate) return _session is not null; }
        }

        public Task<JraSession> GetSessionAsync(CancellationToken cancellationToken)
        {
            lock (_gate)
                return _session ??= Factory.CreateAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            Task<JraSession>? session;
            lock (_gate) session = _session;
            if (session is null) return;
            JraSession ownedSession;
            try { ownedSession = await session.ConfigureAwait(false); }
            catch { return; } // Construction failed, so no owned session exists to dispose.
            await ownedSession.DisposeAsync().ConfigureAwait(false);
        }
    }
}

public sealed class JraSessionLease : IAsyncDisposable
{
    internal JraSessionLease(JraSession session, bool ownsSession)
    {
        Session = session;
        _ownsSession = ownsSession;
    }

    private readonly bool _ownsSession;
    public JraSession Session { get; }

    public ValueTask DisposeAsync() => _ownsSession ? Session.DisposeAsync() : ValueTask.CompletedTask;
}
