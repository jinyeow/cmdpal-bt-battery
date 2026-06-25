namespace BtBattery.Abstractions;

/// <summary>
/// The single seam quarantining the hard-to-test OS Bluetooth dependency from testable logic.
/// GATT is deferred behind this interface.
/// </summary>
public interface IBatteryProvider : IDisposable
{
    /// <summary>
    /// A fresh snapshot of currently connected monitored devices. Cancellation applies to the
    /// snapshot only and is best-effort over native enumeration.
    /// </summary>
    Task<IReadOnlyList<MonitoredDevice>> GetConnectedDevicesAsync(CancellationToken ct = default);

    /// <summary>
    /// Raised when the snapshot may have changed (carries no payload). May fire on ANY thread and is
    /// coalesced; the caller re-queries via <see cref="GetConnectedDevicesAsync"/> and marshals.
    /// Never raised after <see cref="IDisposable.Dispose"/>. Handlers must be cheap and non-blocking
    /// (schedule the re-query elsewhere) — they may run while the provider holds an internal lock.
    /// </summary>
    event EventHandler DevicesInvalidated;

    /// <summary>Starts the device watcher. Idempotent.</summary>
    void StartWatching();

    /// <summary>Stops the device watcher. Idempotent.</summary>
    void StopWatching();
}
