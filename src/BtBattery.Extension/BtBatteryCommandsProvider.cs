using BtBattery.Abstractions;
using BtBattery.Extension.Pages;
using BtBattery.Windows;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace BtBattery.Extension;

public sealed partial class BtBatteryCommandsProvider : CommandProvider, IDisposable
{
    private readonly BtBatteryListPage _listPage;
    private readonly WrappedDockItem _dockBand;
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

        _dockBand = new WrappedDockItem(
            BuildDockItems(BatterySummary.Empty.Rows),
            "BtBattery.listPage",
            "Bluetooth Battery")
        {
            Icon = new IconInfo(""),
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
        return [_dockBand];
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
        bool changed = !SummaryContentEquals(summary, _lastPublished);
        _lastPublished = summary;
        if (!changed) return;

        try { _dockBand.Items = BuildDockItems(summary.Rows); }
        catch (Exception ex) { Trace.TraceWarning(ex.ToString()); }

        try { _listPage.NotifySummaryChanged(); }
        catch (Exception ex) { Trace.TraceWarning(ex.ToString()); }
    }

    private static IListItem[] BuildDockItems(IReadOnlyList<MonitoredDevice> rows)
    {
        if (rows.Count == 0)
        {
            return [new ListItem(new NoOpCommand() { Id = "BtBattery.listPage" })
            {
                Title = "Bluetooth Battery",
                Subtitle = "—",
                Icon = new IconInfo(""),
            }];
        }

        return [..rows.Select(d => (IListItem)new ListItem(new NoOpCommand() { Id = "BtBattery.listPage" })
        {
            Title = d.Battery.State == BatteryState.Known ? $"{d.Battery.Percent}%" : "—",
            Subtitle = d.DisplayName,
            Icon = new IconInfo(DeviceCategoryGlyph.For(d.Category)),
        })];
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
