namespace BtBattery.Abstractions;

/// <summary>Icon hint for a monitored device. Not authoritative — display only.</summary>
public enum DeviceCategory
{
    Other,
    Headset,
    Mouse,
    Keyboard,
}

/// <summary>
/// One physical Bluetooth device the extension shows, identified by its container id.
/// Earbud-pair and dual-mode (Classic + LE) endpoints of one physical device collapse into one.
/// </summary>
public sealed record MonitoredDevice(
    string ContainerId,
    string DisplayName,
    DeviceCategory Category,
    bool IsConnected,
    BatteryLevel Battery,
    bool HasMultipleBatteryValues);
