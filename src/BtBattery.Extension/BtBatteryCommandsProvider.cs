using BtBattery.Abstractions;
using BtBattery.Extension.Pages;
using BtBattery.Windows;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;

namespace BtBattery.Extension;

public sealed partial class BtBatteryCommandsProvider : CommandProvider, IDisposable
{
    private readonly BtBatteryListPage _listPage = new();
    private readonly ListItem _dockItem;
    private readonly DeviceInformationBatteryProvider _btProvider = new();
    private readonly RefreshCoordinator _coordinator;
    private bool _started;

    public BtBatteryCommandsProvider()
    {
        Id = "BtBattery";
        DisplayName = "Bluetooth Battery";
        Icon = new IconInfo("");

        _dockItem = new ListItem(_listPage)
        {
            Title = "Bluetooth Battery",
            Icon = new IconInfo(""),
            Subtitle = "—",
        };

        _coordinator = new RefreshCoordinator(
            _btProvider,
            lowThreshold: 20,
            TimeProvider.System,
            publish: OnSummaryPublished,
            debounceWindow: TimeSpan.FromMilliseconds(250),
            fallbackInterval: TimeSpan.FromMinutes(5));

        _listPage.Coordinator = _coordinator;
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

        _started = true;
        try
        {
            _coordinator.Start();
        }
        catch (Exception)
        {
            // Watcher-start failure is non-fatal; on-open refresh via GetItems() still works.
        }
    }

    private void OnSummaryPublished(BatterySummary summary)
    {
        try { _dockItem.Subtitle = summary.DockTitle; } catch { }
        try { _listPage.NotifySummaryChanged(summary); } catch { }
    }

    public override void Dispose()
    {
        _coordinator.Dispose();
        _btProvider.Dispose();
    }
}
