using BtBattery.Abstractions;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System.Collections.Generic;

namespace BtBattery.Extension.Pages;

/// <summary>
/// Top-level list page: searchable full device detail view.
/// Items are populated from the coordinator's current <see cref="BatterySummary"/> on each open.
/// </summary>
public sealed partial class BtBatteryListPage : ListPage
{
    private readonly Func<BatterySummary> _getCurrent;

    public BtBatteryListPage(Func<BatterySummary> getCurrent)
    {
        _getCurrent = getCurrent;
        Name = "Bluetooth Battery";
        Icon = new IconInfo(""); // Bluetooth glyph
    }

    public override IListItem[] GetItems()
    {
        BatterySummary summary = _getCurrent();
        return BuildItems(summary);
    }

    internal void NotifySummaryChanged() => RaiseItemsChanged(0);

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
