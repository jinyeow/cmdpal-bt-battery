using BtBattery.Abstractions;
using Xunit;

namespace BtBattery.Tests;

public sealed class BatterySummaryContentEqualsTests
{
    private const int LowThreshold = 20;

    private static MonitoredDevice Device(string name, BatteryLevel battery) =>
        new(
            ContainerId: name,
            DisplayName: name,
            Category: DeviceCategory.Other,
            Battery: battery,
            HasMultipleBatteryValues: false);

    [Fact]
    public void IdenticalSummaries_ReturnsTrue()
    {
        MonitoredDevice mouse = Device("Mouse", BatteryLevel.Known(60));
        BatterySummary a = BatterySummary.Compute([mouse], LowThreshold);
        BatterySummary b = BatterySummary.Compute([mouse], LowThreshold);

        Assert.True(BatterySummary.ContentEquals(a, b));
    }

    [Fact]
    public void DifferentStatusLine_ReturnsFalse()
    {
        BatterySummary a = BatterySummary.Compute([Device("Mouse", BatteryLevel.Known(60))], LowThreshold);
        BatterySummary b = BatterySummary.Compute([Device("Earbuds", BatteryLevel.Known(30))], LowThreshold);

        Assert.False(BatterySummary.ContentEquals(a, b));
    }

    [Fact]
    public void DifferentLowCount_ReturnsFalse()
    {
        // Only the LowCount and StatusLine differ: isolate by constructing directly.
        MonitoredDevice mouse = Device("Mouse", BatteryLevel.Known(50));
        IReadOnlyList<MonitoredDevice> rows = [mouse];
        BatterySummary a = new(mouse, 0, rows, "50% Mouse");
        BatterySummary b = new(mouse, 1, rows, "50% Mouse");

        Assert.False(BatterySummary.ContentEquals(a, b));
    }

    [Fact]
    public void DifferentRowCount_ReturnsFalse()
    {
        MonitoredDevice mouse = Device("Mouse", BatteryLevel.Known(60));
        MonitoredDevice keyboard = Device("Keyboard", BatteryLevel.Known(80));
        BatterySummary a = BatterySummary.Compute([mouse], LowThreshold);
        BatterySummary b = BatterySummary.Compute([mouse, keyboard], LowThreshold);

        Assert.False(BatterySummary.ContentEquals(a, b));
    }

    [Fact]
    public void SameRowCount_OneDeviceBatteryPercentChanged_ReturnsFalse()
    {
        BatterySummary a = BatterySummary.Compute([Device("Mouse", BatteryLevel.Known(60))], LowThreshold);
        BatterySummary b = BatterySummary.Compute([Device("Mouse", BatteryLevel.Known(70))], LowThreshold);

        Assert.False(BatterySummary.ContentEquals(a, b));
    }

    [Fact]
    public void SameRowCount_OneDeviceDisplayNameChanged_ReturnsFalse()
    {
        BatterySummary a = BatterySummary.Compute([Device("Mouse", BatteryLevel.Known(60))], LowThreshold);
        BatterySummary b = BatterySummary.Compute([Device("Keyboard", BatteryLevel.Known(60))], LowThreshold);

        Assert.False(BatterySummary.ContentEquals(a, b));
    }
}
