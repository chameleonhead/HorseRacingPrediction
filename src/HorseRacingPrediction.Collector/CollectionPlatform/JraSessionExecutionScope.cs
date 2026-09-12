using System.Threading;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Scraping.Jra;

namespace HorseRacingPrediction.Collector.CollectionPlatform;

/// <summary>
/// Shares one JRA browser session for the duration of a compatible collection envelope.
/// AsyncLocal keeps concurrent Lambda/local-queue executions isolated from one another.
/// </summary>
public static class JraSessionExecutionScope
{
    private static readonly AsyncLocal<ScopeState?> Current = new();

    public static async Task ExecuteAsync(IJraSessionFactory factory, Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken, CollectionDispatchCompatibilityKey? compatibilityKey = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(operation);

        if (Current.Value is not null)
        {
            await operation(cancellationToken).ConfigureAwait(false);
            return;
        }

        var state = new ScopeState(factory, compatibilityKey);
        Current.Value = state;
        try
        {
            await operation(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Current.Value = null;
            await state.DisposeAsync().ConfigureAwait(false);
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
