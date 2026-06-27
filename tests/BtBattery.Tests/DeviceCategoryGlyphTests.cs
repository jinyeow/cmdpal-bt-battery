using BtBattery.Abstractions;
using Xunit;

namespace BtBattery.Tests;

public sealed class DeviceCategoryGlyphTests
{
    [Theory]
    [InlineData(DeviceCategory.Headset)]
    [InlineData(DeviceCategory.Mouse)]
    [InlineData(DeviceCategory.Keyboard)]
    [InlineData(DeviceCategory.Other)]
    public void For_EveryCategory_ReturnsNonEmptyString(DeviceCategory category)
    {
        string glyph = DeviceCategoryGlyph.For(category);

        Assert.False(string.IsNullOrEmpty(glyph));
    }

    [Fact]
    public void For_AllCategories_ReturnDistinctGlyphs()
    {
        DeviceCategory[] all = [
            DeviceCategory.Headset,
            DeviceCategory.Mouse,
            DeviceCategory.Keyboard,
            DeviceCategory.Other,
        ];

        string[] glyphs = Array.ConvertAll(all, DeviceCategoryGlyph.For);

        Assert.Equal(glyphs.Length, glyphs.Distinct().Count());
    }

    [Fact]
    public void For_Headset_ReturnsSegoeAudioHeadphoneGlyph()
    {
        // Segoe MDL2 Assets U+E7EF: AudioHeadphone
        Assert.Equal("", DeviceCategoryGlyph.For(DeviceCategory.Headset));
    }

    [Fact]
    public void For_Other_ReturnsSegoeBluetoothGlyph()
    {
        // Segoe MDL2 Assets U+E702: Bluetooth
        Assert.Equal("", DeviceCategoryGlyph.For(DeviceCategory.Other));
    }
}
