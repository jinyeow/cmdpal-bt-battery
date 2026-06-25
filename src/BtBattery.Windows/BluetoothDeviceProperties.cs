namespace BtBattery.Windows;

/// <summary>
/// PnP property keys used to enumerate Bluetooth device battery + identity.
/// Confirmed by the console spike against real hardware (HONOR earbuds via HFP, MX Master 3 via LE).
/// </summary>
internal static class BluetoothDeviceProperties
{
    /// <summary>
    /// Bluetooth battery DEVPKEY (DEVPKEY_Bluetooth_Battery), value type <c>Byte</c>. Present only on
    /// the specific child node that reports battery (HFP for Classic, BAS for LE); absent elsewhere.
    /// Cannot be used in an AQS filter ({GUID} PID keys are filter-ineligible) — request as a property.
    /// </summary>
    public const string Battery = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    public const string ContainerId = "System.Devices.ContainerId";
    public const string DeviceInstanceId = "System.Devices.DeviceInstanceId";
    public const string ClassGuid = "System.Devices.ClassGuid";

    /// <summary>Properties requested for every enumerated <c>Kind.Device</c> Bluetooth node.</summary>
    public static readonly string[] DeviceQuery =
    [
        Battery,
        ContainerId,
        DeviceInstanceId,
        ClassGuid,
    ];

    /// <summary>
    /// AQS selector for Bluetooth PnP device nodes, matched on the instance-id prefix
    /// (BTHENUM = Classic, BTHLE/BTHLEDEVICE = LE, BTHHFENUM = hands-free, BTH\MS_* = local radio).
    /// </summary>
    public const string BluetoothDeviceSelector = "System.Devices.DeviceInstanceId:~<\"BTH\"";

    /// <summary>AQS selector for currently-connected device containers.</summary>
    public const string ConnectedContainerSelector =
        "System.Devices.Connected:=System.StructuredQueryType.Boolean#True";

    /// <summary>
    /// Instance-id prefix of the local Microsoft Bluetooth radio stack. A container that owns any
    /// such node is the adapter itself (e.g. a USB dongle), not a peripheral — exclude it.
    /// </summary>
    public const string LocalRadioInstancePrefix = "BTH\\MS_";

    /// <summary>HID device setup class — distinguishes mouse/keyboard peripherals.</summary>
    public static readonly Guid HidClass = new("745a17a0-74d3-11d0-b6fe-00a0c90f57da");

    /// <summary>Media/audio device setup class — distinguishes headsets/earphones.</summary>
    public static readonly Guid MediaClass = new("4d36e96c-e325-11ce-bfc1-08002be10318");
}
