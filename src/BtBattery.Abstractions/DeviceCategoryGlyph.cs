namespace BtBattery.Abstractions;

/// <summary>Maps <see cref="DeviceCategory"/> to a Segoe MDL2 Assets glyph character.</summary>
public static class DeviceCategoryGlyph
{
    // U+E7EF AudioHeadphone, U+E962 Mouse, U+E765 Keyboard, U+E702 Bluetooth
    private const string HeadsetGlyph   = "";
    private const string MouseGlyph     = "";
    private const string KeyboardGlyph  = "";
    private const string BluetoothGlyph = "";

    public static string For(DeviceCategory category) => category switch
    {
        DeviceCategory.Headset  => HeadsetGlyph,
        DeviceCategory.Mouse    => MouseGlyph,
        DeviceCategory.Keyboard => KeyboardGlyph,
        _                       => BluetoothGlyph,
    };
}
