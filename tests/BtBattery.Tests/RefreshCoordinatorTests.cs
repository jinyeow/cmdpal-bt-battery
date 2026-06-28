using BtBattery.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace BtBattery.Tests;

public sealed class RefreshCoordinatorTests
{
    private const int LowThreshold = 20;
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan Fallback = TimeSpan.FromMinutes(5);

    private static MonitoredDevice Device(string name, BatteryLevel battery) =>
        new(
            ContainerId: name,
            DisplayName: name,
            Category: DeviceCategory.Other,
            Battery: battery,
            HasMultipleBatteryValues: false);

    [Fact]
    public async Task RefreshNow_QueriesProvider_PublishesComputedSummary()
    {
        var provider = new FakeProvider();
        provider.Devices = [Device("Earbuds", BatteryLevel.Known(15))];
        var published = new List<BatterySummary>();
        var time = new FakeTimeProvider();
        using var coordinator = new RefreshCoordinator(
            provider, LowThreshold, time, published.Add, Debounce, Fallback);

        await coordinator.RefreshNowAsync();

        BatterySummary summary = Assert.Single(published);
        Assert.Equal("15% Earbuds", summary.StatusLine);
        Assert.Equal(summary, coordinator.Current);
        Assert.Equal(1, provider.QueryCount);
    }

    [Fact]
    public async Task RefreshNow_SerializesOverlappingRefreshes()
    {
        var provider = new FakeProvider { Devices = [Device("Mouse", BatteryLevel.Known(50))] };
        var published = new List<BatterySummary>();
        var time = new FakeTimeProvider();
        using var coordinator = new RefreshCoordinator(
            provider, LowThreshold, time, published.Add, Debounce, Fallback);

        provider.Block();
        Task first = coordinator.RefreshNowAsync();
        Task second = coordinator.RefreshNowAsync();

        await Task.Yield();
        Assert.Equal(1, provider.QueryCount); // second is parked behind the gate, hasn't queried

        provider.Release();
        await Task.WhenAll(first, second);

        Assert.Equal(2, provider.QueryCount);
        Assert.Equal(2, published.Count);
    }

    [Fact]
    public async Task Invalidation_TriggersRefreshAfterDebounceWindow()
    {
        var provider = new FakeProvider { Devices = [Device("Mouse", BatteryLevel.Known(50))] };
        var published = new List<BatterySummary>();
        var time = new FakeTimeProvider();
        using var coordinator = new RefreshCoordinator(
            provider, LowThreshold, time, published.Add, Debounce, Fallback);
        coordinator.Start();

        provider.RaiseInvalidated();
        Assert.Equal(0, provider.QueryCount); // still inside the debounce window

        time.Advance(Debounce);
        await Task.Yield();

        Assert.Equal(1, provider.QueryCount);
        Assert.Single(published);
    }

    [Fact]
    public async Task BurstyInvalidations_CollapseToOneRefresh_WindowRestartsEachEvent()
    {
        var provider = new FakeProvider { Devices = [Device("Mouse", BatteryLevel.Known(50))] };
        var published = new List<BatterySummary>();
        var time = new FakeTimeProvider();
        using var coordinator = new RefreshCoordinator(
            provider, LowThreshold, time, published.Add, Debounce, Fallback);
        coordinator.Start();
        TimeSpan half = TimeSpan.FromTicks(Debounce.Ticks / 2);

        provider.RaiseInvalidated();
        provider.RaiseInvalidated();
        provider.RaiseInvalidated();
        time.Advance(half);
        Assert.Equal(0, provider.QueryCount);

        provider.RaiseInvalidated(); // restarts the debounce window
        time.Advance(half);
        Assert.Equal(0, provider.QueryCount); // last event reset the window; full span not yet elapsed

        time.Advance(half);
        await Task.Yield();
        Assert.Equal(1, provider.QueryCount); // the whole burst yielded exactly one refresh
        Assert.Single(published);
    }

    [Fact]
    public async Task RequestsDuringInFlightRefresh_CollapseToExactlyOneMore()
    {
        var provider = new FakeProvider { Devices = [Device("Mouse", BatteryLevel.Known(50))] };
        var published = new List<BatterySummary>();
        var time = new FakeTimeProvider();
        using var coordinator = new RefreshCoordinator(
            provider, LowThreshold, time, published.Add, Debounce, Fallback);
        coordinator.Start();

        provider.Block();
        provider.RaiseInvalidated();
        time.Advance(Debounce); // first background refresh starts and blocks inside the provider
        await Task.Yield();
        Assert.Equal(1, provider.QueryCount);

        for (int i = 0; i < 3; i++)
        {
            provider.RaiseInvalidated();
            time.Advance(Debounce); // each fires while the first refresh is still in flight
        }

        await Task.Yield();
        Assert.Equal(1, provider.QueryCount); // still blocked: requests were coalesced, not queued

        provider.Release();
        await WaitForAsync(() => provider.QueryCount == 2, "exactly one more refresh after the in-flight one");

        Assert.Equal(2, provider.QueryCount); // running one + exactly one more, never three
        Assert.Equal(2, published.Count);
    }

    [Fact]
    public async Task LatestResultWins_FinalSummaryReflectsMostRecentSnapshot()
    {
        var provider = new FakeProvider { Devices = [Device("Mouse", BatteryLevel.Known(90))] };
        var published = new List<BatterySummary>();
        var time = new FakeTimeProvider();
        using var coordinator = new RefreshCoordinator(
            provider, LowThreshold, time, published.Add, Debounce, Fallback);
        coordinator.Start();

        provider.Block();
        provider.RaiseInvalidated();
        time.Advance(Debounce); // run 1 starts, captures the 90% snapshot, then blocks
        await Task.Yield();

        provider.Devices = [Device("Mouse", BatteryLevel.Known(10))]; // device drained while run 1 is in flight
        provider.RaiseInvalidated();
        time.Advance(Debounce); // schedules exactly one more run, which will capture the 10% snapshot

        provider.Release();
        await WaitForAsync(() => published.Count == 2, "both refreshes published");

        Assert.Equal("90% Mouse", published[0].StatusLine);  // in-order, serialized
        Assert.Equal("10% Mouse", published[^1].StatusLine);  // latest snapshot wins
        Assert.Equal(published[^1], coordinator.Current);
    }

    [Fact]
    public async Task FallbackTimer_TriggersPeriodicRefresh()
    {
        var provider = new FakeProvider { Devices = [Device("Mouse", BatteryLevel.Known(50))] };
        var published = new List<BatterySummary>();
        var time = new FakeTimeProvider();
        using var coordinator = new RefreshCoordinator(
            provider, LowThreshold, time, published.Add, Debounce, Fallback);
        coordinator.Start();

        Assert.Equal(0, provider.QueryCount);

        time.Advance(Fallback);
        await Task.Yield();
        Assert.Equal(1, provider.QueryCount);

        time.Advance(Fallback);
        await Task.Yield();
        Assert.Equal(2, provider.QueryCount); // fires on every interval, not just once
    }

    [Fact]
    public async Task DisposeMidRefresh_PublishesNothingAndDoesNotThrow()
    {
        var provider = new FakeProvider { Devices = [Device("Mouse", BatteryLevel.Known(50))] };
        var published = new List<BatterySummary>();
        var time = new FakeTimeProvider();
        var coordinator = new RefreshCoordinator(
            provider, LowThreshold, time, published.Add, Debounce, Fallback);
        coordinator.Start();

        provider.Block();
        provider.RaiseInvalidated();
        time.Advance(Debounce); // background refresh starts and blocks inside the provider
        await Task.Yield();
        Assert.Equal(1, provider.QueryCount);

        coordinator.Dispose(); // dispose while the refresh is still in flight
        provider.Release();    // let the blocked enumeration unwind

        await Task.Delay(50); // give any erroneous publish a chance to surface
        Assert.Empty(published);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var provider = new FakeProvider();
        var time = new FakeTimeProvider();
        var coordinator = new RefreshCoordinator(
            provider, LowThreshold, time, _ => { }, Debounce, Fallback);
        coordinator.Start();

        coordinator.Dispose();
        coordinator.Dispose(); // must not throw
    }

    [Fact]
    public async Task InvalidationAfterDispose_DoesNotRefresh()
    {
        var provider = new FakeProvider { Devices = [Device("Mouse", BatteryLevel.Known(50))] };
        var published = new List<BatterySummary>();
        var time = new FakeTimeProvider();
        var coordinator = new RefreshCoordinator(
            provider, LowThreshold, time, published.Add, Debounce, Fallback);
        coordinator.Start();
        coordinator.Dispose();

        provider.RaiseInvalidated(); // unsubscribed at dispose; ignored
        time.Advance(Debounce);
        await Task.Yield();

        Assert.Equal(0, provider.QueryCount);
        Assert.Empty(published);
    }

    [Fact]
    public async Task Start_WatcherFails_UnsubscribesAndAllowsRetry()
    {
        var provider = new FakeProvider { Devices = [Device("Mouse", BatteryLevel.Known(50))] };
        provider.FailNextStartWatching();
        var published = new List<BatterySummary>();
        var time = new FakeTimeProvider();
        using var coordinator = new RefreshCoordinator(
            provider, LowThreshold, time, published.Add, Debounce, Fallback);

        Assert.Throws<InvalidOperationException>(() => coordinator.Start());

        // After a failed Start, invalidations must not trigger refreshes (handler should be unsubscribed)
        provider.RaiseInvalidated();
        time.Advance(Debounce);
        await Task.Yield();
        Assert.Equal(0, provider.QueryCount);

        // Retry must succeed; fallback timer fires and watcher is started exactly once
        coordinator.Start();
        Assert.Equal(1, provider.WatchCount);
        time.Advance(Fallback);
        await Task.Yield();
        Assert.Equal(1, provider.QueryCount);
        Assert.Single(published);
    }

    [Fact]
    public async Task BackgroundRefresh_ProviderFault_CoordinatorStillFunctional()
    {
        var provider = new FakeProvider { Devices = [Device("Mouse", BatteryLevel.Known(50))] };
        var published = new List<BatterySummary>();
        var time = new FakeTimeProvider();
        using var coordinator = new RefreshCoordinator(
            provider, LowThreshold, time, published.Add, Debounce, Fallback);
        coordinator.Start();

        provider.FaultNext(); // first query will throw InvalidOperationException

        provider.RaiseInvalidated();
        time.Advance(Debounce);
        await WaitForAsync(() => provider.QueryCount >= 1, "faulting refresh to run");

        // _backgroundRunning must be false after the fault; the next trigger should start a new refresh
        provider.RaiseInvalidated();
        time.Advance(Debounce);
        await WaitForAsync(() => published.Count == 1, "second refresh to publish");

        Assert.Single(published);
        Assert.Equal(2, provider.QueryCount);
    }

    [Fact]
    public async Task Start_IsIdempotent()
    {
        var provider = new FakeProvider { Devices = [Device("Mouse", BatteryLevel.Known(50))] };
        var published = new List<BatterySummary>();
        var time = new FakeTimeProvider();
        using var coordinator = new RefreshCoordinator(
            provider, LowThreshold, time, published.Add, Debounce, Fallback);

        coordinator.Start();
        coordinator.Start(); // second call must not re-subscribe or re-start the watcher

        provider.RaiseInvalidated();
        time.Advance(Debounce);
        await Task.Yield();

        Assert.Equal(1, provider.WatchCount); // StartWatching called exactly once
        Assert.Equal(1, provider.QueryCount);
        Assert.Single(published);
    }

    [Fact]
    public async Task RefreshNow_PreCancelledToken_ThrowsAndGateNeverAcquired()
    {
        var provider = new FakeProvider { Devices = [Device("Mouse", BatteryLevel.Known(50))] };
        var published = new List<BatterySummary>();
        var time = new FakeTimeProvider();
        using var coordinator = new RefreshCoordinator(
            provider, LowThreshold, time, published.Add, Debounce, Fallback);

        // A pre-cancelled token causes WaitAsync to throw before acquiring the semaphore,
        // so the gate remains available for the subsequent call.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.RefreshNowAsync(new CancellationToken(canceled: true)));

        await coordinator.RefreshNowAsync(CancellationToken.None);

        Assert.Single(published);
    }

    private static async Task WaitForAsync(Func<bool> condition, string because)
    {
        for (int i = 0; i < 200; i++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"Timed out waiting for: {because}");
    }

    /// <summary>Fake provider whose enumeration can be gated to simulate in-flight refreshes.</summary>
    private sealed class FakeProvider : IBatteryProvider
    {
        private TaskCompletionSource? _gate;
        private bool _faultNext;
        private bool _failNextStartWatching;

        public IReadOnlyList<MonitoredDevice> Devices { get; set; } = [];
        public int QueryCount { get; private set; }
        public int WatchCount { get; private set; }

        public async Task<IReadOnlyList<MonitoredDevice>> GetConnectedDevicesAsync(CancellationToken ct = default)
        {
            QueryCount++;
            if (_faultNext)
            {
                _faultNext = false;
                throw new InvalidOperationException("Simulated provider fault.");
            }

            IReadOnlyList<MonitoredDevice> snapshot = Devices; // capture at enumeration start, like a real provider
            TaskCompletionSource? gate = _gate;
            if (gate is not null)
            {
                await gate.Task.WaitAsync(ct);
            }

            return snapshot;
        }

        /// <summary>Blocks subsequent enumerations until <see cref="Release"/> is called.</summary>
        public void Block() => _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release()
        {
            _gate?.TrySetResult();
            _gate = null;
        }

        /// <summary>Makes the next <see cref="GetConnectedDevicesAsync"/> call throw <see cref="InvalidOperationException"/>.</summary>
        public void FaultNext() => _faultNext = true;

        /// <summary>Makes the next <see cref="StartWatching"/> call throw <see cref="InvalidOperationException"/>.</summary>
        public void FailNextStartWatching() => _failNextStartWatching = true;

        public event EventHandler? DevicesInvalidated;

        public void RaiseInvalidated() => DevicesInvalidated?.Invoke(this, EventArgs.Empty);

        public void StartWatching()
        {
            if (_failNextStartWatching)
            {
                _failNextStartWatching = false;
                throw new InvalidOperationException("Simulated StartWatching failure.");
            }

            WatchCount++;
        }

        public void StopWatching() { }

        public void Dispose() { }
    }
}
