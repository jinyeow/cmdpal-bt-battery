using BtBattery.Abstractions;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Collections.Generic;

namespace BtBattery.Extension.Pages;

/// <summary>
/// Top-level list page: searchable full device detail view.
/// Items are populated from <see cref="RefreshCoordinator.Current"/> on each open.
/// </summary>
public sealed class BtBatteryListPage : ListPage
{
    // Set by BtBatteryCommandsProvider after construction.
    internal RefreshCoordinator? Coordinator { get; set; }

    public BtBatteryListPage()
    {
        Name = "Bluetooth Battery";
        Icon = new IconInfo(""); // Bluetooth glyph
    }

    public override IListItem[] GetItems()
    {
        // Refresh synchronously on open so the user sees fresh data immediately.
        if (Coordinator is not null)
        {
            try
            {
                Coordinator.RefreshNowAsync().GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // Enumeration fault — show stale/empty data rather than crash.
            }
        }

        BatterySummary summary = Coordinator?.Current ?? BatterySummary.Empty;
        return BuildItems(summary);
    }

    internal void NotifySummaryChanged(BatterySummary summary) => RaiseItemsChanged(0);

    private static IListItem[] BuildItems(BatterySummary summary)
    {
        if (summary.Rows.Count == 0)
        {
            return [new ListItem(new NoOpCommand()) { Title = "No connected Bluetooth devices" }];
        }

        List<IListItem> items = new(summary.Rows.Count);
        foreach (MonitoredDevice device in summary.Rows)
        {
            string batteryText = device.Battery.State == BatteryState.Known
                ? $"{device.Battery.Percent}%"
                : "—";
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = device.DisplayName,
                Subtitle = batteryText,
                Icon = new IconInfo(DeviceCategoryGlyph.For(device.Category)),
            });
        }

        return [.. items];
    }
}
