using BtBattery.Abstractions;
using Xunit;

namespace BtBattery.Tests;

public sealed class BatterySummaryComputeTests
{
    private const int LowThreshold = 20;

    private static MonitoredDevice Device(
        string name,
        BatteryLevel battery,
        DeviceCategory category = DeviceCategory.Other,
        bool hasMultiple = false) =>
        new(
            ContainerId: name,
            DisplayName: name,
            Category: category,
            IsConnected: true,
            Battery: battery,
            HasMultipleBatteryValues: hasMultiple);

    [Fact]
    public void EmptyInput_ReturnsSilentSummary()
    {
        BatterySummary summary = BatterySummary.Compute([], LowThreshold);

        Assert.Null(summary.Headline);
        Assert.Empty(summary.Rows);
        Assert.False(summary.HasLowDevice);
        Assert.Equal(0, summary.LowCount);
        Assert.Equal(string.Empty, summary.DockTitle);
    }

    [Fact]
    public void Headline_IsLowestKnownBattery()
    {
        MonitoredDevice low = Device("Earbuds", BatteryLevel.Known(35));
        MonitoredDevice high = Device("Mouse", BatteryLevel.Known(90));

        BatterySummary summary = BatterySummary.Compute([high, low], LowThreshold);

        Assert.Equal(low, summary.Headline);
        Assert.Equal("35% Earbuds", summary.DockTitle);
    }

    [Fact]
    public void Headline_ExcludesUnknownFromCalc()
    {
        MonitoredDevice unknown = Device("Keyboard", BatteryLevel.Unknown);
        MonitoredDevice known = Device("Mouse", BatteryLevel.Known(55));

        BatterySummary summary = BatterySummary.Compute([unknown, known], LowThreshold);

        Assert.Equal(known, summary.Headline);
    }

    [Fact]
    public void AllUnknown_HasNoHeadlineButStillListsRows()
    {
        MonitoredDevice a = Device("Mouse", BatteryLevel.Unknown);
        MonitoredDevice b = Device("Keyboard", BatteryLevel.Unknown);

        BatterySummary summary = BatterySummary.Compute([a, b], LowThreshold);

        Assert.Null(summary.Headline);
        Assert.Equal(2, summary.Rows.Count);
        Assert.False(summary.HasLowDevice);
        Assert.Equal("—", summary.DockTitle);
    }

    [Fact]
    public void ThreeLowDevices_AppendPlusTwo()
    {
        MonitoredDevice lowest = Device("Earbuds", BatteryLevel.Known(5));
        MonitoredDevice low2 = Device("Mouse", BatteryLevel.Known(12));
        MonitoredDevice low3 = Device("Keyboard", BatteryLevel.Known(18));

        BatterySummary summary = BatterySummary.Compute([low2, lowest, low3], LowThreshold);

        Assert.Equal(3, summary.LowCount);
        Assert.Equal("5% Earbuds +2", summary.DockTitle);
    }

    [Fact]
    public void LowCount_ExcludesUnknownDevices()
    {
        MonitoredDevice low = Device("Earbuds", BatteryLevel.Known(10));
        MonitoredDevice unknownA = Device("Mouse", BatteryLevel.Unknown);
        MonitoredDevice unknownB = Device("Keyboard", BatteryLevel.Unknown);

        BatterySummary summary = BatterySummary.Compute([low, unknownA, unknownB], LowThreshold);

        Assert.Equal(1, summary.LowCount);
        Assert.True(summary.HasLowDevice);
        Assert.Equal(low, summary.Headline);
    }

    [Fact]
    public void KnownZero_HeadlinesAndCountsAsLow()
    {
        MonitoredDevice flat = Device("Earbuds", BatteryLevel.Known(0));
        MonitoredDevice normal = Device("Mouse", BatteryLevel.Known(80));

        BatterySummary summary = BatterySummary.Compute([normal, flat], LowThreshold);

        Assert.Equal(flat, summary.Headline);
        Assert.Equal("0% Earbuds", summary.DockTitle);
        Assert.Equal(1, summary.LowCount);
        Assert.True(summary.HasLowDevice);
    }

    [Fact]
    public void Rows_PreserveInputOrderOnEqualPercent()
    {
        MonitoredDevice first = Device("First", BatteryLevel.Known(40));
        MonitoredDevice second = Device("Second", BatteryLevel.Known(40));

        BatterySummary summary = BatterySummary.Compute([first, second], LowThreshold);

        Assert.Equal(["First", "Second"], summary.Rows.Select(r => r.DisplayName));
    }

    [Fact]
    public void ThresholdBoundary_TwentyPercentIsLow()
    {
        MonitoredDevice atThreshold = Device("Earbuds", BatteryLevel.Known(20));

        BatterySummary summary = BatterySummary.Compute([atThreshold], LowThreshold);

        Assert.True(summary.HasLowDevice);
        Assert.Equal(1, summary.LowCount);
    }

    [Fact]
    public void JustAboveThreshold_IsNotLow()
    {
        MonitoredDevice aboveThreshold = Device("Earbuds", BatteryLevel.Known(21));

        BatterySummary summary = BatterySummary.Compute([aboveThreshold], LowThreshold);

        Assert.False(summary.HasLowDevice);
        Assert.Equal(0, summary.LowCount);
    }

    [Fact]
    public void SingleLowDevice_HasNoPlusSuffix()
    {
        MonitoredDevice low = Device("Earbuds", BatteryLevel.Known(10));
        MonitoredDevice normal = Device("Mouse", BatteryLevel.Known(80));

        BatterySummary summary = BatterySummary.Compute([low, normal], LowThreshold);

        Assert.Equal("10% Earbuds", summary.DockTitle);
    }

    [Fact]
    public void TwoLowDevices_AppendPlusOne()
    {
        MonitoredDevice lowest = Device("Earbuds", BatteryLevel.Known(8));
        MonitoredDevice alsoLow = Device("Mouse", BatteryLevel.Known(15));
        MonitoredDevice normal = Device("Keyboard", BatteryLevel.Known(80));

        BatterySummary summary = BatterySummary.Compute([alsoLow, lowest, normal], LowThreshold);

        Assert.Equal(2, summary.LowCount);
        Assert.Equal("8% Earbuds +1", summary.DockTitle);
    }

    [Fact]
    public void Rows_SortKnownLowThenKnownNormalThenUnknownLast()
    {
        MonitoredDevice unknown = Device("Webcam", BatteryLevel.Unknown);
        MonitoredDevice normal = Device("Mouse", BatteryLevel.Known(70));
        MonitoredDevice low = Device("Earbuds", BatteryLevel.Known(12));

        BatterySummary summary = BatterySummary.Compute([unknown, normal, low], LowThreshold);

        Assert.Equal(["Earbuds", "Mouse", "Webcam"], summary.Rows.Select(r => r.DisplayName));
    }

    [Fact]
    public void Rows_PreserveDeviceFlags()
    {
        MonitoredDevice multi = Device("Earbuds", BatteryLevel.Known(40), hasMultiple: true);

        BatterySummary summary = BatterySummary.Compute([multi], LowThreshold);

        Assert.True(summary.Rows.Single().HasMultipleBatteryValues);
    }
}
