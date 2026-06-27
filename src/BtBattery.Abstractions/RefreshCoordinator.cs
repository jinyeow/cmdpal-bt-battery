using System.Diagnostics;

namespace BtBattery.Abstractions;

/// <summary>
/// Owns the three refresh triggers (on-open, watcher invalidation, fallback timer), serializing them
/// so overlapping refreshes can't publish stale results out of order. Debounces bursty invalidations,
/// runs exactly one more refresh if one is requested mid-run, and publishes only the latest result via
/// an injected marshal delegate (so this stays WinUI-free and unit-testable).
/// </summary>
public sealed class RefreshCoordinator : IDisposable
{
    private readonly IBatteryProvider _provider;
    private readonly int _lowThreshold;
    private readonly Action<BatterySummary> _publish;
    private readonly TimeSpan _debounceWindow;
    private readonly TimeSpan _fallbackInterval;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ITimer _debounceTimer;
    private readonly ITimer _fallbackTimer;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _lock = new();

    private bool _started;
    private bool _backgroundRunning;
    private bool _runAgain;
    private bool _disposed;

    public RefreshCoordinator(
        IBatteryProvider provider,
        int lowThreshold,
        TimeProvider timeProvider,
        Action<BatterySummary> publish,
        TimeSpan debounceWindow,
        TimeSpan fallbackInterval)
    {
        _provider = provider;
        _lowThreshold = lowThreshold;
        _publish = publish;
        _debounceWindow = debounceWindow;
        _fallbackInterval = fallbackInterval;
        _debounceTimer = timeProvider.CreateTimer(
            _ => RequestBackgroundRefresh(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _fallbackTimer = timeProvider.CreateTimer(
            _ => RequestBackgroundRefresh(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>The latest published summary. <see cref="BatterySummary.Empty"/> until the first refresh.</summary>
    public BatterySummary Current { get; private set; } = BatterySummary.Empty;

    /// <summary>Subscribes to invalidations and starts the device watcher. Must be called exactly once.</summary>
    public void Start()
    {
        lock (_lock)
        {
            if (_started || _disposed)
            {
                return;
            }

            _started = true;
        }

        try
        {
            // StartWatching first: if it throws we have nothing to roll back.
            // Early watcher events that arrive before the subscription are tolerable —
            // the fallback timer ensures the next refresh catches up.
            _provider.StartWatching();
            _provider.DevicesInvalidated += OnDevicesInvalidated;
            _fallbackTimer.Change(_fallbackInterval, _fallbackInterval);
        }
        catch
        {
            // Roll back so a retry can succeed. -=  is a no-op if += was never reached.
            _provider.DevicesInvalidated -= OnDevicesInvalidated;
            lock (_lock) { _started = false; }
            throw;
        }
    }

    /// <summary>The on-open trigger: awaitable so a page can render against fresh data.</summary>
    public async Task RefreshNowAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            IReadOnlyList<MonitoredDevice> devices = await _provider.GetConnectedDevicesAsync(ct);
            BatterySummary summary = BatterySummary.Compute(devices, _lowThreshold);

            lock (_lock)
            {
                if (_disposed)
                {
                    return; // never publish after disposal
                }

                Current = summary;
            }

            _publish(summary);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnDevicesInvalidated(object? sender, EventArgs e)
    {
        // Coalesce a burst of invalidations into one refresh: each event restarts the debounce window.
        _debounceTimer.Change(_debounceWindow, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Schedules a background refresh, coalescing into "the running one + at most one more": any number
    /// of requests arriving while a refresh is in flight collapse to a single additional run.
    /// </summary>
    private void RequestBackgroundRefresh()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            if (_backgroundRunning)
            {
                _runAgain = true;
                return;
            }

            _backgroundRunning = true;
        }

        _ = RunBackgroundLoopAsync();
    }

    private async Task RunBackgroundLoopAsync()
    {
        while (true)
        {
            try
            {
                await RefreshNowAsync(_cts.Token);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                // Expected: disposal cancelled the in-flight refresh.
            }
            catch (Exception ex)
            {
                // Enumeration fault; retry on next trigger. Fire-and-forget must never throw into the host.
                // TODO: structured logging when the Extension wires a logger.
                Trace.TraceError(ex.ToString());
            }

            lock (_lock)
            {
                if (_disposed || !_runAgain)
                {
                    _backgroundRunning = false;
                    return;
                }

                _runAgain = false;
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _provider.DevicesInvalidated -= OnDevicesInvalidated;
        _provider.StopWatching();
        _debounceTimer.Dispose();
        _fallbackTimer.Dispose();
        _cts.Cancel();
        _cts.Dispose();

        // _gate is intentionally not disposed: an in-flight background refresh releases it in its
        // finally after cancellation unwinds, which would race a Dispose() here. SemaphoreSlim needs
        // disposal only when its AvailableWaitHandle was accessed (it never is), so this is safe.
    }
}
