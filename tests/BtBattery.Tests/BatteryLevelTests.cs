using BtBattery.Abstractions;
using Xunit;

namespace BtBattery.Tests;

public sealed class BatteryLevelTests
{
    [Fact]
    public void Known_StoresStateAndPercent()
    {
        BatteryLevel level = BatteryLevel.Known(50);

        Assert.Equal(BatteryState.Known, level.State);
        Assert.Equal(50, level.Percent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void Known_AcceptsBoundaryValues(int percent)
    {
        BatteryLevel level = BatteryLevel.Known(percent);

        Assert.Equal(percent, level.Percent);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Known_RejectsOutOfRange(int percent)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BatteryLevel.Known(percent));
    }

    [Fact]
    public void Unknown_IsDistinctFromKnownZero()
    {
        Assert.Equal(BatteryState.Unknown, BatteryLevel.Unknown.State);
        Assert.NotEqual(BatteryLevel.Known(0), BatteryLevel.Unknown);
    }

    [Fact]
    public void Default_IsUnknownNotKnownZero()
    {
        BatteryLevel uninitialized = default;

        Assert.Equal(BatteryState.Unknown, uninitialized.State);
        Assert.Equal(BatteryLevel.Unknown, uninitialized);
    }
}
