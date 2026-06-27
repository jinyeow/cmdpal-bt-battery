using BtBattery.Abstractions;
using BtBattery.Extension.Pages;
using BtBattery.Windows;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Diagnostics;
using System.Linq;

namespace BtBattery.Extension;

public sealed partial class BtBatteryCommandsProvider : CommandProvider, IDisposable
{
    private readonly BtBatteryListPage _listPage;
    private readonly ListItem _dockItem;
    private readonly DeviceInformationBatteryProvider _btProvider = new();
    private readonly RefreshCoordinator _coordinator;
    private bool _started;
    private BatterySummary _lastPublished = BatterySummary.Empty;

    public BtBatteryCommandsProvider()
    {
        Id = "BtBattery";
        DisplayName = "Bluetooth Battery";
        Icon = new IconInfo("");

        _coordinator = new RefreshCoordinator(
            _btProvider,
            lowThreshold: 20,
            TimeProvider.System,
            publish: OnSummaryPublished,
            debounceWindow: TimeSpan.FromMilliseconds(250),
            fallbackInterval: TimeSpan.FromMinutes(5));

        _listPage = new BtBatteryListPage(
            getCurrent: () => _coordinator.Current,
            requestRefresh: () => _ = _coordinator.RefreshNowAsync());

        _dockItem = new ListItem(new NoOpCommand() { Id = "BtBattery.listPage" })
        {
            Title = "Bluetooth Battery",
            Icon = new IconInfo(""),
            Subtitle = "—",
        };
    }

    public override ICommandItem[] TopLevelCommands()
    {
        EnsureStarted();
        return [new ListItem(_listPage) { Title = "Bluetooth Battery", Icon = new IconInfo("") }];
    }

    public override ICommandItem[] GetDockBands()
    {
        EnsureStarted();
        return [new WrappedDockItem([_dockItem], "BtBattery.listPage", "Bluetooth Battery") { Icon = new IconInfo("") }];
    }

    private void EnsureStarted()
    {
        if (_started)
        {
            return;
        }

        try
        {
            _coordinator.Start();
            _started = true;
        }
        catch (Exception ex)
        {
            // Watcher-start failure is non-fatal; allow retry on next call.
            Trace.TraceWarning(ex.ToString());
        }
    }

    private void OnSummaryPublished(BatterySummary summary)
    {
        try
        {
            if (summary.Headline is MonitoredDevice hd)
            {
                _dockItem.Title = $"{hd.Battery.Percent}%";
                _dockItem.Subtitle = hd.DisplayName;
            }
            else
            {
                _dockItem.Title = "Bluetooth Battery";
                _dockItem.Subtitle = "—";
            }
        }
        catch (Exception ex) { Trace.TraceWarning(ex.ToString()); }

        // Guard: only re-render the list if content actually changed.
        // Without this, GetItems() → _requestRefresh() → OnSummaryPublished (unchanged) →
        // NotifySummaryChanged() → GetItems() creates an infinite BT enumeration loop.
        bool changed = !SummaryContentEquals(summary, _lastPublished);
        _lastPublished = summary;
        if (!changed) return;

        try { _listPage.NotifySummaryChanged(); }
        catch (Exception ex) { Trace.TraceWarning(ex.ToString()); }
    }

    private static bool SummaryContentEquals(BatterySummary a, BatterySummary b) =>
        a.DockTitle == b.DockTitle &&
        a.LowCount == b.LowCount &&
        a.Rows.Count == b.Rows.Count &&
        a.Rows.SequenceEqual(b.Rows);

    public override void Dispose()
    {
        _coordinator.Dispose();
        _btProvider.Dispose();
    }
}
