using BtBattery.Abstractions;
using Windows.Devices.Enumeration;

namespace BtBattery.Windows;

/// <summary>
/// Reads connected Bluetooth device battery levels from the cached PnP device property that feeds
/// Windows Settings. Battery + identity come from <c>Kind.Device</c> Bluetooth nodes; connection
/// state and display name come from the correlated <c>Kind.DeviceContainer</c> (the spike confirmed
/// connection state lives only on the container). Child devnodes collapse by container id; the local
/// radio adapter is excluded.
/// </summary>
public sealed class DeviceInformationBatteryProvider : IBatteryProvider
{
    private readonly object _gate = new();
    private DeviceWatcher? _deviceWatcher;
    private DeviceWatcher? _containerWatcher;
    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler? DevicesInvalidated;

    /// <inheritdoc />
    public async Task<IReadOnlyList<MonitoredDevice>> GetConnectedDevicesAsync(CancellationToken ct = default)
    {
        // The two enumerations are independent (battery on device nodes, connection state on
        // containers), so run them concurrently and correlate once both complete.
        Task<DeviceInformationCollection> deviceNodesTask = DeviceInformation.FindAllAsync(
            BluetoothDeviceProperties.BluetoothDeviceSelector,
            BluetoothDeviceProperties.DeviceQuery,
            DeviceInformationKind.Device).AsTask(ct);

        Task<DeviceInformationCollection> connectedContainersTask = DeviceInformation.FindAllAsync(
            BluetoothDeviceProperties.ConnectedContainerSelector,
            null,
            DeviceInformationKind.DeviceContainer).AsTask(ct);

        await Task.WhenAll(deviceNodesTask, connectedContainersTask).ConfigureAwait(false);

        return Aggregate(deviceNodesTask.Result, connectedContainersTask.Result);
    }

    /// <inheritdoc />
    public void StartWatching()
    {
        lock (_gate)
        {
            if (_disposed || _deviceWatcher is not null)
            {
                return;
            }

            // Two watchers feed the same invalidation signal. The device-node watcher surfaces
            // battery/pairing changes; the connected-container watcher surfaces connect/disconnect —
            // connection state lives only on the container (Added = connected, Removed = disconnected),
            // never on the device node, so the device watcher alone would miss those transitions.
            DeviceWatcher? deviceWatcher = null;
            DeviceWatcher? containerWatcher = null;
            try
            {
                deviceWatcher = CreateWatcher(
                    BluetoothDeviceProperties.BluetoothDeviceSelector,
                    BluetoothDeviceProperties.DeviceQuery,
                    DeviceInformationKind.Device);
                containerWatcher = CreateWatcher(
                    BluetoothDeviceProperties.ConnectedContainerSelector,
                    null,
                    DeviceInformationKind.DeviceContainer);

                deviceWatcher.Start();
                containerWatcher.Start();
            }
            catch
            {
                // Roll back so the provider stays fully stopped (fields null) and a later
                // StartWatching can retry, rather than being left half-started.
                StopWatcher(deviceWatcher);
                StopWatcher(containerWatcher);
                throw;
            }

            _deviceWatcher = deviceWatcher;
            _containerWatcher = containerWatcher;
        }
    }

    /// <inheritdoc />
    public void StopWatching()
    {
        DeviceWatcher? deviceWatcher;
        DeviceWatcher? containerWatcher;
        lock (_gate)
        {
            deviceWatcher = _deviceWatcher;
            containerWatcher = _containerWatcher;
            _deviceWatcher = null;
            _containerWatcher = null;
        }

        StopWatcher(deviceWatcher);
        StopWatcher(containerWatcher);
    }

    private DeviceWatcher CreateWatcher(string selector, string[]? additionalProperties, DeviceInformationKind kind)
    {
        DeviceWatcher watcher = DeviceInformation.CreateWatcher(selector, additionalProperties, kind);
        watcher.Added += OnDeviceAdded;
        watcher.Updated += OnDeviceUpdated;
        watcher.Removed += OnDeviceRemoved;
        return watcher;
    }

    private void StopWatcher(DeviceWatcher? watcher)
    {
        if (watcher is null)
        {
            return;
        }

        watcher.Added -= OnDeviceAdded;
        watcher.Updated -= OnDeviceUpdated;
        watcher.Removed -= OnDeviceRemoved;

        if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
        {
            watcher.Stop();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        StopWatching();
        DevicesInvalidated = null;
    }

    private void OnDeviceAdded(DeviceWatcher sender, DeviceInformation args) => RaiseInvalidated();

    private void OnDeviceUpdated(DeviceWatcher sender, DeviceInformationUpdate args) => RaiseInvalidated();

    private void OnDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate args) => RaiseInvalidated();

    private void RaiseInvalidated()
    {
        // Invoke under the lock so a concurrent Dispose cannot slip between the _disposed check and
        // the callback: a raise already in flight completes before Dispose can return, and any raise
        // that starts after Dispose sees _disposed and bails. This honors the IBatteryProvider
        // "never raised after Dispose" contract. Safe because the handler is contractually a cheap,
        // coalescing signal (it re-queries asynchronously, it does not block here).
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            DevicesInvalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    private static IReadOnlyList<MonitoredDevice> Aggregate(
        IEnumerable<DeviceInformation> deviceNodes,
        IEnumerable<DeviceInformation> connectedContainers)
    {
        Dictionary<string, string> connectedById = new(StringComparer.OrdinalIgnoreCase);
        foreach (DeviceInformation container in connectedContainers)
        {
            connectedById[container.Id] = container.Name;
        }

        Dictionary<string, List<DeviceInformation>> byContainer = new(StringComparer.OrdinalIgnoreCase);
        foreach (DeviceInformation node in deviceNodes)
        {
            string? containerId = ReadContainerId(node);
            if (string.IsNullOrEmpty(containerId))
            {
                continue;
            }

            if (!byContainer.TryGetValue(containerId, out List<DeviceInformation>? children))
            {
                children = [];
                byContainer[containerId] = children;
            }

            children.Add(node);
        }

        List<MonitoredDevice> devices = [];
        foreach ((string containerId, List<DeviceInformation> children) in byContainer)
        {
            if (IsLocalRadioContainer(children) || !connectedById.TryGetValue(containerId, out string? name))
            {
                continue;
            }

            int[] knownPercents = children
                .Select(ReadBatteryPercent)
                .Where(p => p.HasValue)
                .Select(p => p!.Value)
                .ToArray();

            BatteryLevel battery = knownPercents.Length > 0
                ? BatteryLevel.Known(knownPercents.Min())
                : BatteryLevel.Unknown;

            devices.Add(new MonitoredDevice(
                ContainerId: containerId,
                DisplayName: name,
                Category: ResolveCategory(children, name),
                IsConnected: true,
                Battery: battery,
                HasMultipleBatteryValues: knownPercents.Distinct().Count() > 1));
        }

        return devices;
    }

    private static bool IsLocalRadioContainer(IEnumerable<DeviceInformation> children) =>
        children.Any(c =>
            ReadString(c, BluetoothDeviceProperties.DeviceInstanceId)
                .StartsWith(BluetoothDeviceProperties.LocalRadioInstancePrefix, StringComparison.OrdinalIgnoreCase));

    private static DeviceCategory ResolveCategory(IEnumerable<DeviceInformation> children, string name)
    {
        Guid[] classes = children.Select(ReadClassGuid).ToArray();
        if (classes.Contains(BluetoothDeviceProperties.MediaClass))
        {
            return DeviceCategory.Headset;
        }

        if (classes.Contains(BluetoothDeviceProperties.HidClass))
        {
            return name.Contains("keyboard", StringComparison.OrdinalIgnoreCase)
                ? DeviceCategory.Keyboard
                : DeviceCategory.Mouse;
        }

        if (name.Contains("keyboard", StringComparison.OrdinalIgnoreCase))
        {
            return DeviceCategory.Keyboard;
        }

        if (name.Contains("mouse", StringComparison.OrdinalIgnoreCase))
        {
            return DeviceCategory.Mouse;
        }

        if (name.Contains("earbud", StringComparison.OrdinalIgnoreCase)
            || name.Contains("earphone", StringComparison.OrdinalIgnoreCase)
            || name.Contains("headset", StringComparison.OrdinalIgnoreCase)
            || name.Contains("headphone", StringComparison.OrdinalIgnoreCase))
        {
            return DeviceCategory.Headset;
        }

        return DeviceCategory.Other;
    }

    private static string? ReadContainerId(DeviceInformation info)
    {
        if (!info.Properties.TryGetValue(BluetoothDeviceProperties.ContainerId, out object? value) || value is null)
        {
            return null;
        }

        return value is Guid guid ? guid.ToString("B") : value.ToString();
    }

    private static int? ReadBatteryPercent(DeviceInformation info)
    {
        // The Battery DEVPKEY is type Byte (0-255). Treat a missing key, an unexpected type, or an
        // out-of-range reading (e.g. a 0xFF "unknown" sentinel from misbehaving firmware) as absent,
        // so the device degrades to Unknown instead of throwing out of enumeration.
        if (!info.Properties.TryGetValue(BluetoothDeviceProperties.Battery, out object? value) || value is not byte percent)
        {
            return null;
        }

        return percent <= 100 ? percent : null;
    }

    private static Guid ReadClassGuid(DeviceInformation info) =>
        info.Properties.TryGetValue(BluetoothDeviceProperties.ClassGuid, out object? value) && value is Guid guid
            ? guid
            : Guid.Empty;

    private static string ReadString(DeviceInformation info, string key) =>
        info.Properties.TryGetValue(key, out object? value) && value is string s ? s : string.Empty;
}
