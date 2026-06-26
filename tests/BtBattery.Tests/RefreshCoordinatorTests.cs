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
            IsConnected: true,
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
        Assert.Equal("15% Earbuds", summary.DockTitle);
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

        Assert.Equal("90% Mouse", published[0].DockTitle);  // in-order, serialized
        Assert.Equal("10% Mouse", published[^1].DockTitle);  // latest snapshot wins
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

        public IReadOnlyList<MonitoredDevice> Devices { get; set; } = [];
        public int QueryCount { get; private set; }

        public async Task<IReadOnlyList<MonitoredDevice>> GetConnectedDevicesAsync(CancellationToken ct = default)
        {
            QueryCount++;
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

        public event EventHandler? DevicesInvalidated;

        public void RaiseInvalidated() => DevicesInvalidated?.Invoke(this, EventArgs.Empty);

        public void StartWatching() { }

        public void StopWatching() { }

        public void Dispose() { }
    }
}
