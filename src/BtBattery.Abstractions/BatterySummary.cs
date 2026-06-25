namespace BtBattery.Abstractions;

/// <summary>
/// The computed, display-ready view over all monitored devices. Produced by the pure
/// <see cref="Compute"/> function — the deep module of the system.
/// </summary>
public sealed record BatterySummary(
    MonitoredDevice? Headline,
    int LowCount,
    IReadOnlyList<MonitoredDevice> Rows,
    string DockTitle)
{
    /// <summary>True when at least one device reports a Known battery at/below the low threshold.</summary>
    public bool HasLowDevice => LowCount > 0;

    /// <summary>The empty/silent summary (Bluetooth off or no connected devices).</summary>
    public static readonly BatterySummary Empty =
        new(null, 0, [], string.Empty);

    /// <summary>Dock title shown when devices are connected but none report a Known battery.</summary>
    private const string NeutralTitle = "—";

    /// <summary>
    /// Pure, stateless projection of a device snapshot into a <see cref="BatterySummary"/>.
    /// Headline = lowest <em>Known</em> battery (Unknown excluded from the calc); a device is low
    /// when its Known percent is at/below <paramref name="lowThreshold"/>; <c>+N</c> appears when 2+
    /// devices are low; rows sort known-low → known-normal → Unknown last.
    /// </summary>
    public static BatterySummary Compute(IReadOnlyList<MonitoredDevice> devices, int lowThreshold)
    {
        if (devices.Count == 0)
        {
            return Empty;
        }

        IReadOnlyList<MonitoredDevice> rows = devices
            .Select((device, index) => (device, index))
            .OrderBy(x => x.device.Battery.State == BatteryState.Known ? 0 : 1)
            .ThenBy(x => x.device.Battery.State == BatteryState.Known ? x.device.Battery.Percent : int.MaxValue)
            .ThenBy(x => x.index)
            .Select(x => x.device)
            .ToArray();

        MonitoredDevice[] known = devices
            .Where(d => d.Battery.State == BatteryState.Known)
            .ToArray();

        MonitoredDevice? headline = known
            .OrderBy(d => d.Battery.Percent)
            .FirstOrDefault();

        int lowCount = known.Count(d => d.Battery.Percent <= lowThreshold);

        string dockTitle;
        if (headline is null)
        {
            dockTitle = NeutralTitle;
        }
        else
        {
            dockTitle = $"{headline.Battery.Percent}% {headline.DisplayName}";
            if (lowCount >= 2)
            {
                dockTitle += $" +{lowCount - 1}";
            }
        }

        return new BatterySummary(headline, lowCount, rows, dockTitle);
    }
}
