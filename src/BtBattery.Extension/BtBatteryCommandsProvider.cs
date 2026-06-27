using BtBattery.Abstractions;
using BtBattery.Extension.Pages;
using BtBattery.Windows;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Diagnostics;

namespace BtBattery.Extension;

public sealed partial class BtBatteryCommandsProvider : CommandProvider, IDisposable
{
    private readonly BtBatteryListPage _listPage;
    private readonly ListItem _dockItem;
    private readonly DeviceInformationBatteryProvider _btProvider = new();
    private readonly RefreshCoordinator _coordinator;
    private bool _started;

    public BtBatteryCommandsProvider()
    {
        Id = "BtBattery";
        DisplayName = "Bluetooth Battery";
        Icon = new IconInfo("");

        _coordinator = new RefreshCoordinator(
            _btProvider,
            lowThreshold: 20,
            TimeProvider.System,
            publish: OnSummaryPublished,
            debounceWindow: TimeSpan.FromMilliseconds(250),
            fallbackInterval: TimeSpan.FromMinutes(5));

        _listPage = new BtBatteryListPage(() => _coordinator.Current);

        _dockItem = new ListItem(_listPage)
        {
            Title = "Bluetooth Battery",
            Icon = new IconInfo(""),
            Subtitle = "—",
        };
    }

    public override ICommandItem[] TopLevelCommands()
    {
        EnsureStarted();
        return [new ListItem(_listPage) { Title = "Bluetooth Battery" }];
    }

    public override ICommandItem[] GetDockBands()
    {
        EnsureStarted();
        return [new WrappedDockItem([_dockItem], "BtBattery.dock", "Bluetooth Battery")];
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
        try { _dockItem.Subtitle = summary.DockTitle; }
        catch (Exception ex) { Trace.TraceWarning(ex.ToString()); }

        try { _listPage.NotifySummaryChanged(); }
        catch (Exception ex) { Trace.TraceWarning(ex.ToString()); }
    }

    public override void Dispose()
    {
        _coordinator.Dispose();
        _btProvider.Dispose();
    }
}
