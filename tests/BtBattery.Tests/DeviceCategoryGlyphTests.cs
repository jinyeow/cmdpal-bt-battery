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
        Assert.Equal("", DeviceCategoryGlyph.For(DeviceCategory.Headset));
    }

    [Fact]
    public void For_Mouse_ReturnsSegoeMouse2Glyph()
    {
        Assert.Equal("", DeviceCategoryGlyph.For(DeviceCategory.Mouse));
    }

    [Fact]
    public void For_Keyboard_ReturnsSegoeKeyboardClassicGlyph()
    {
        Assert.Equal("", DeviceCategoryGlyph.For(DeviceCategory.Keyboard));
    }

    [Fact]
    public void For_Other_ReturnsSegoeBluetoothGlyph()
    {
        Assert.Equal("", DeviceCategoryGlyph.For(DeviceCategory.Other));
    }
}
