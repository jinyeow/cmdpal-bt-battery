using BtBattery.Abstractions;
using BtBattery.Windows;
using Windows.Devices.Enumeration;

// Console enumeration spike — the gating artifact (PRD → Further Notes → Spike first).
// Goal: empirically resolve the battery data-source unknowns on REAL connected hardware, and
// exercise the exact provider code that ships (DeviceInformationBatteryProvider).
//
// ⚠ Connect your Bluetooth earphones (and any mouse/keyboard that matters) BEFORE running.
// The spike only sees what is connected at the moment it runs.

const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2"; // candidate DEVPKEY_Bluetooth_Battery

string[] deviceProps =
[
    BatteryKey,
    "System.Devices.ContainerId",
    "System.Devices.Connected",
    "System.Devices.DeviceInstanceId",
    "System.Devices.ClassGuid",
    "System.Devices.PrimaryCategory",
];

string[] containerProps =
[
    "System.Devices.Connected",
    "System.Devices.ModelName",
    "System.Devices.Category",
];

string[] aepProps =
[
    "System.Devices.Aep.Battery",
    "System.Devices.Aep.IsConnected",
    "System.Devices.Aep.IsPaired",
    "System.Devices.Aep.Category",
    "System.Devices.Aep.ContainerId",
];

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("=== BtBattery enumeration spike ===");
Console.WriteLine($"Run time (UTC): {DateTimeOffset.UtcNow:O}\n");

// ---- Section A: raw Kind.Device dump for Bluetooth PnP devices -----------------------------------
Console.WriteLine("---- [A] Kind.Device (selector: instance-id starts with BTH) ----");
DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(
    "System.Devices.DeviceInstanceId:~<\"BTH\"", deviceProps, DeviceInformationKind.Device);
Console.WriteLine($"Matched {devices.Count} device node(s).\n");
foreach (DeviceInformation d in devices)
{
    DumpProperties($"Device: {d.Name}", d);
}

// Fallback: if the targeted selector matched nothing, dump ALL Kind.Device that expose the battery
// key, so we still discover where battery lives even if the BTH instance-id filter is wrong.
if (devices.Count == 0)
{
    Console.WriteLine("(!) BTH selector matched nothing — falling back to ALL Kind.Device with a battery value.\n");
    DeviceInformationCollection all = await DeviceInformation.FindAllAsync(
        string.Empty, deviceProps, DeviceInformationKind.Device);
    foreach (DeviceInformation d in all)
    {
        if (d.Properties.TryGetValue(BatteryKey, out object? v) && v is not null)
        {
            DumpProperties($"Device(battery): {d.Name}", d);
        }
    }
}

// ---- Section B: raw Kind.DeviceContainer dump ---------------------------------------------------
Console.WriteLine("\n---- [B] Kind.DeviceContainer (connected) ----");
try
{
    DeviceInformationCollection containers = await DeviceInformation.FindAllAsync(
        "System.Devices.Connected:=System.StructuredQueryType.Boolean#True",
        containerProps, DeviceInformationKind.DeviceContainer);
    Console.WriteLine($"Matched {containers.Count} connected container(s).\n");
    foreach (DeviceInformation c in containers)
    {
        DumpProperties($"Container: {c.Name}", c);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"(!) Container query failed: {ex.GetType().Name}: {ex.Message}\n");
}

// ---- Section C: raw Kind.AssociationEndpoint dump (Bluetooth + BLE) ------------------------------
Console.WriteLine("\n---- [C] Kind.AssociationEndpoint (Bluetooth selectors) ----");
foreach ((string label, string selector) in new[]
{
    ("Classic", Windows.Devices.Bluetooth.BluetoothDevice.GetDeviceSelectorFromPairingState(true)),
    ("LE", Windows.Devices.Bluetooth.BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)),
})
{
    try
    {
        DeviceInformationCollection aeps = await DeviceInformation.FindAllAsync(selector, aepProps);
        Console.WriteLine($"[{label}] matched {aeps.Count} endpoint(s).");
        foreach (DeviceInformation a in aeps)
        {
            DumpProperties($"AEP[{label}]: {a.Name}", a);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"(!) AEP[{label}] query failed: {ex.GetType().Name}: {ex.Message}");
    }
}

// ---- Section D: exercise the SHIPPING provider --------------------------------------------------
Console.WriteLine("\n---- [D] DeviceInformationBatteryProvider.GetConnectedDevicesAsync ----");
using (IBatteryProvider provider = new DeviceInformationBatteryProvider())
{
    IReadOnlyList<MonitoredDevice> monitored = await provider.GetConnectedDevicesAsync();
    Console.WriteLine($"Provider returned {monitored.Count} connected monitored device(s):\n");
    foreach (MonitoredDevice m in monitored)
    {
        string battery = m.Battery.State == BatteryState.Known ? $"{m.Battery.Percent}%" : "Unknown";
        string multi = m.HasMultipleBatteryValues ? " (multiple battery values collapsed)" : string.Empty;
        Console.WriteLine($"  • {m.DisplayName} [{m.Category}] — {battery}{multi}");
        Console.WriteLine($"      containerId={m.ContainerId} connected={m.IsConnected}");
    }

    // ---- Section E: watcher smoke test ----------------------------------------------------------
    // Confirms the watcher fires on connect/disconnect (the container watcher), not just battery.
    // An initial enumeration burst fires on StartWatching; after it settles, DISCONNECT then
    // RECONNECT a device and watch for a fresh invalidation on each transition.
    const int watchSeconds = 20;
    Console.WriteLine($"\n---- [E] Watcher ({watchSeconds}s) — after the initial burst, DISCONNECT then RECONNECT a device ----");
    int invalidations = 0;
    provider.DevicesInvalidated += (_, _) =>
    {
        int n = Interlocked.Increment(ref invalidations);
        Console.WriteLine($"    [{DateTimeOffset.Now:HH:mm:ss}] invalidation #{n}");
    };
    provider.StartWatching();
    await Task.Delay(TimeSpan.FromSeconds(watchSeconds));
    provider.StopWatching();
    Console.WriteLine($"Total invalidation events in window: {invalidations}");
}

Console.WriteLine("\n=== Spike complete. Review [A]–[D] to resolve the 8 unknowns. ===");
return;

static void DumpProperties(string header, DeviceInformation info)
{
    Console.WriteLine(header);
    Console.WriteLine($"    Id={info.Id}");
    Console.WriteLine($"    Kind={info.Kind} Enabled={info.IsEnabled}");
    foreach (KeyValuePair<string, object> kv in info.Properties)
    {
        if (kv.Value is null)
        {
            continue;
        }

        string rendered = kv.Value is IEnumerable<string> many ? string.Join(", ", many) : kv.Value.ToString() ?? "";
        Console.WriteLine($"    {kv.Key} = {rendered}  ({kv.Value.GetType().Name})");
    }

    Console.WriteLine();
}
