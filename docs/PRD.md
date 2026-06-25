# PRD — BtBattery: Command Palette Bluetooth Battery Monitor (v1)

> Status: ready-for-agent
> Surface: PowerToys Command Palette (CmdPal) extension — Dock band + top-level list page
> Platform: Windows 11, C# / .NET 8, WinUI + Windows App SDK, MSIX-packaged

## Problem Statement

As a Windows 11 power user who works almost entirely from the keyboard through PowerToys
Command Palette, I have no fast, glanceable way to see the battery levels of my **connected
Bluetooth devices** (earphones, mouse, keyboard). Windows does know these values — they show in
Settings → Bluetooth & devices — but checking them means breaking out of my keyboard-driven flow,
opening Settings, and hunting for the number. By the time I think to check, my earbuds are already
dying mid-call. I want the battery number where I already look: the Command Palette and its Dock.

## Solution

A Command Palette extension that monitors connected Bluetooth devices and surfaces their battery
levels in two places:

- A persistent **Dock band** that headlines the single most-urgent battery at a glance (the lowest
  known level across connected devices), with a flyout that lists every connected device and its
  level on demand.
- A searchable **top-level list page** inside the palette for the full detail view, usable even
  when the Dock is disabled.

It reads the **same battery values Windows itself caches** (the per-device battery property that
feeds Settings), so it never disagrees with the system, needs no pairing dance or active Bluetooth
session, covers both Classic (BR/EDR, e.g. headsets) and LE devices, and stays completely silent
when nothing is connected.

## Glossary (terms used throughout)

- **Monitored device** — one physical Bluetooth device the extension shows, identified by its
  **container id** (see below). Earbud-pair endpoints and dual-mode (Classic + LE) endpoints of the
  same physical device collapse into one monitored device.
- **Container id** — the Windows PnP `System.Devices.ContainerId`. For Bluetooth it is derived from
  the device MAC address, so every devnode/service of one physical device shares it. Used as both
  the **dedup key** and the **persistence key**.
- **Battery level** — a value that is either **Known (0–100%)** or **Unknown**. Unknown (property
  absent) is a first-class state, distinct from a real 0%.
- **Battery summary** — the computed, display-ready view over all monitored devices: the headline
  device, the count of devices below the low threshold, the ordered row list, and the dock-band
  title string.
- **Dock band** — the persistent toolbar widget the extension contributes via `GetDockBands()`.
- **Low threshold** — the percentage at/below which a device is "low" (hardcoded **20%** in v1).

## User Stories

1. As a CmdPal user, I want to see my connected Bluetooth devices' battery levels inside the
   palette, so that I never have to open Windows Settings to check them.
2. As a CmdPal user, I want a persistent Dock band showing the lowest battery across my connected
   devices, so that I get an at-a-glance "is anything about to die?" signal without opening anything.
3. As a CmdPal user, I want the Dock band to show the device name behind the lowest level, so that I
   know *which* device needs attention.
4. As a CmdPal user, I want to click the Dock band and see a flyout of every connected device with
   its battery level, so that I can see the full picture on demand.
5. As a CmdPal user, I want a searchable top-level list page ("Bluetooth battery"), so that I can
   reach the full detail view even when the Dock is disabled.
6. As a user with wireless earphones, I want their battery level shown, so that I can charge them
   before a call instead of during one.
7. As a user with a Bluetooth mouse and keyboard, I want their levels shown alongside my earphones,
   so that I have one place for all my accessory batteries.
8. As a user with a dual-mode headset (pairs as both Classic and LE), I want it shown as a single
   row, so that I don't see confusing duplicate entries for one physical device.
9. As a user, I want the value shown to match exactly what Windows Settings shows, so that I never
   have to reconcile two different numbers.
10. As a user, I want devices whose battery Windows cannot report shown as "Unknown" rather than 0%,
    so that I am not misled into thinking a healthy device is dead.
11. As a user, I want the device list sorted with the most urgent (lowest) battery first, so that the
    thing about to die is at the top.
12. As a user, I want "Unknown" devices sorted last, so that they never crowd out the levels I can
    act on.
13. As a user, I want low devices visually emphasized (colored battery tag), so that "low" jumps out
    without me reading numbers.
14. As a user, I want the Dock band emphasized when at least one device is low, so that the toolbar
    itself signals the problem.
15. As a user with two devices low at once, I want the Dock band to headline the lowest *and* show a
    "+N" indicator, so that I know more than one device needs attention without opening the flyout.
16. As a user, I want the battery values to refresh the moment I open the flyout or list page, so
    that what I see is current at the instant I look.
17. As a user, I want the Dock band's headline number to update on its own while it sits in the
    toolbar, so that the glanceable value stays fresh without me opening anything.
18. As a user, I want the extension to use a low-frequency background refresh, so that it stays
    current without wasting battery/CPU polling for a value that changes slowly.
19. As a user, I want the extension to show nothing at all when Bluetooth is off, so that it never
    nags me about a subsystem I have deliberately turned off.
20. As a user, I want the extension to show nothing when no Bluetooth devices are connected, so that
    it stays out of my way until there is actually something to report.
21. As a user, I want a connected device whose battery is temporarily unreadable to still appear (as
    Unknown), so that I can tell it is connected even if its level is momentarily missing.
22. As a user, I want the extension to never crash or freeze the Command Palette if reading devices
    fails, so that one Bluetooth hiccup never breaks my whole launcher.
23. As a user, I want read failures to fail quietly (no error spam) and retry automatically on the
    next refresh, so that transient glitches self-heal.
24. As a user, I want the extension to only show *connected* devices in v1, so that the list reflects
    what I am actually using right now, not every accessory I have ever paired.
25. As a developer/maintainer, I want all the display rules encoded in one pure, unit-tested function,
    so that the behavior is verifiable without a Bluetooth radio or a running palette.
26. As a developer/maintainer, I want the Bluetooth-reading code isolated behind a single interface,
    so that the hard-to-test OS dependency is quarantined from the testable logic.
27. As a developer/maintainer, I want a console spike that reuses the shipping provider code, so that
    what I prove on real hardware is exactly what ships.
28. As a developer/maintainer, I want the three refresh triggers serialized through one coordinator,
    so that overlapping refreshes can't publish stale results out of order.
29. As a developer/maintainer, I want the watcher/refresh lifecycle to be idempotent and safe under
    the out-of-process CmdPal activation model, so that repeated start/stop/teardown never races.
30. As a developer/maintainer, I want a second dock layout (per-device button strip) to be an
    additive change behind a factory seam, so that I can add it later without reworking the core.
31. As a future user, I want my eventual custom device names / hidden devices to survive reconnects,
    so that the extension remembers my preferences — enabled by keying persistence on container id.

## Implementation Decisions

### Data source
- **Single source for v1: Windows device-property enumeration.** Read the cached battery DEVPKEY
  that Windows itself populates (the value Settings shows), via
  `DeviceInformation.FindAllAsync(aqs, additionalProperties)`. This covers **both** Classic BR/EDR
  (HFP-reported) and LE (BAS-reported) devices.
- **GATT is explicitly deferred** behind the provider interface — a possible later augmentation for
  a device that reports via GATT but not the property. Not in v1.
- The exact battery DEVPKEY string and which `DeviceInformationKind` carries it are **confirmed by
  the spike before any extension code is written** (candidate: `{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2`).

### Device identity, scope, dedup
- **Connected-only** in v1.
- **One row per container id** (`System.Devices.ContainerId`). Because the Bluetooth container id is
  MAC-derived, dual-mode and multi-endpoint representations of one physical device collapse by
  construction.
- **Battery is aggregated across child devnodes** of a container — battery and connection state may
  live on different nodes. When a container exposes multiple conflicting battery values (e.g.
  earbuds L/R/case), show the **lowest** and set a `HasMultipleBatteryValues` flag.
- **Persistence key = container id** (for future custom names / hidden devices). Acceptable caveat:
  container id can rotate on firmware reset / LE address change / replacing one earbud.
- **PnP model is the source of truth** (enumerate `Kind.Device` for battery; correlate
  `Kind.DeviceContainer` for `System.Devices.Connected`). The spike confirms whether a single query
  yields both before committing to a two-query design. AssociationEndpoint model is **not** used as
  primary (`AepContainer.IsPresent` ≠ PnP "connected").

### Refresh model
- **Hybrid**: fresh snapshot **on open** (flyout/list page) + a **lazy, disposable `DeviceWatcher`**
  that fires *invalidation* (drives the live dock-band headline) + a **5-minute fallback timer**.
- A **`RefreshCoordinator`** (in the extension layer) owns all three triggers: it debounces watcher
  bursts, serializes refreshes with a semaphore, runs exactly one more if a refresh is requested
  mid-run, publishes only the latest completed result, and marshals to the WinUI thread at the
  boundary.

### Surface & presentation
- **Dock band = single expandable flyout band** (`IContentPage`). Headlines the **lowest *Known*
  battery** (Unknown values excluded from the calc). Appends a **`+N` suffix** when 2+ devices are
  below the low threshold (N = additional low devices beyond the headlined one). Emphasis (icon/
  color where the dock surface supports it) when ≥1 device is low.
- **Top-level list page** for the full detail view, independent of whether the Dock is enabled.
- **Rows**: device-category icon + display name + **colored battery tag** (`%`), with **Unknown**
  shown distinctly ("Unknown"/"—"). Sort: known-low → known-normal → **Unknown last**.
- **Empty/silent** when Bluetooth is off or there are no connected devices (no info rows). A
  user-pinned dock band likely cannot fully vanish; fallback is a **neutral idle glyph** — the
  spike confirms whether `GetDockBands()` can cleanly drop the band.
- Presentation is built behind an **`IDockBandFactory`** seam so a second **per-device button-strip
  layout** (and a layout-toggle setting) is a purely additive change later.

### Settings
- **No settings in v1.** Low threshold hardcoded at **20%**. Per-device customization, layout
  toggle, refresh-interval config all deferred; container-id persistence key keeps them additive.

### Architecture & contracts

Five projects:

- **`BtBattery.Abstractions`** (`net8.0`, no WinRT): domain model, `IBatteryProvider`, the pure
  `Compute` function and `BatterySummary`.
- **`BtBattery.Windows`** (`net8.0-windows10.0.19041`): `DeviceInformationBatteryProvider`
  (enumeration + watcher).
- **`BtBattery.Extension`** (packaged WinUI): `CommandProvider`, pages, `IDockBandFactory` impl(s),
  `RefreshCoordinator`.
- **`BtBattery.Spike`** (console): references `BtBattery.Windows`; the proven enumeration code ships.
- **`BtBattery.Tests`** (`net8.0`): xUnit over the pure logic.

**Locked contract** (shapes came from the design session; trimmed to decision-bearing parts):

```csharp
public enum BatteryState { Known, Unknown }

public readonly record struct BatteryLevel
{
    public BatteryState State { get; }
    public int Percent { get; }                       // 0 when Unknown
    public static readonly BatteryLevel Unknown;
    public static BatteryLevel Known(int percent);    // throws if percent is < 0 or > 100
}

public sealed record MonitoredDevice(
    string ContainerId,
    string DisplayName,
    DeviceCategory Category,        // Headset / Mouse / Keyboard / Other (icon hint)
    bool IsConnected,
    BatteryLevel Battery,
    bool HasMultipleBatteryValues); // conflicting child battery values were collapsed to the lowest

public interface IBatteryProvider : IDisposable
{
    Task<IReadOnlyList<MonitoredDevice>> GetConnectedDevicesAsync(CancellationToken ct = default);
    event EventHandler DevicesInvalidated;  // may fire on ANY thread; coalesced; caller re-queries + marshals
    void StartWatching();                   // idempotent
    void StopWatching();                    // idempotent
}

// Pure, stateless — the deep module:
BatterySummary Compute(IReadOnlyList<MonitoredDevice> devices, int lowThreshold);
```

Contract rules: the invalidation event carries **no payload** (it means "snapshot may have changed —
call `GetConnectedDevicesAsync`"). `StartWatching`/`StopWatching`/`Dispose` are **idempotent** and
must not raise events after disposal (no finalizer). Cancellation applies to the **snapshot only**
and is best-effort over native enumeration. Any future "last-known cache" to smooth Unknown flaps is
an **explicit separate component**, never hidden inside `Compute`.

### Error / edge handling
- The extension must **never throw into the CmdPal host**. Enumeration failures fail quietly,
  structured-log, and retry on the next trigger.
- Bluetooth off / no connected devices → **show nothing**.
- Connected-but-unreadable battery → device still shown, as **Unknown**.
- No fabricated fallback values (no fake 100%/0%) — Unknown is shown honestly.

## Testing Decisions

**What makes a good test here:** assert on **external, observable behavior** — feed fabricated
`MonitoredDevice` snapshots in, assert on the resulting `BatterySummary` / coordinator output. Do
not assert on internal call sequences or private state. `Compute` is pure, so it is tested directly
(no mocking of `Compute` itself); the provider is mocked/faked only at the `IBatteryProvider` seam.

**Modules to be tested (v1):**
1. **`Compute` / `BatterySummary`** — the deep module and primary target. Cover: lowest-Known
   headline; Unknown excluded from the headline calc; `+N` only when 2+ below threshold; emphasis
   when ≥1 low; sort order (known-low → known-normal → Unknown last); empty input; all-Unknown
   input; `HasMultipleBatteryValues` rows; threshold boundary (exactly 20%).
2. **`RefreshCoordinator`** — tested with an **injected clock + fake `IBatteryProvider`**. Cover:
   debounce of bursty invalidations; serialization (no overlapping refreshes); "run one more if
   requested mid-refresh"; latest-result-wins publication; safe behavior on dispose mid-refresh.
3. **`BatteryLevel`** — `Known(int)` rejects values outside 0–100; `Unknown` is distinct from
   `Known(0)`.

**Not unit-tested:** `DeviceInformationBatteryProvider` (OS/hardware-bound) — verified via the
console spike and in-process packaged run. `IDockBandFactory`/pages — presentation glue, low value.

**Prior art:** none — greenfield. This work establishes the project's xUnit convention.

## Out of Scope

- **GATT (active BLE read / BAS notifications)** as a data source — deferred behind `IBatteryProvider`.
- **Disconnected / all-paired device views** — v1 is connected-only.
- **Per-device customization** — rename, hide/exclude, favorite/primary.
- **Per-member earbud battery** (separate L/R/case rows) — v1 shows one value per container; the
  spike only *notes* whether per-member is cheaply available.
- **Low-battery notifications / Windows toasts** — v1 is glance-only; the dock band `+N` emphasis is
  the only alerting surface.
- **Any settings UI** — threshold hardcoded; no settings page in v1.
- **The per-device button-strip dock layout and its toggle** — seam exists (`IDockBandFactory`),
  implementation deferred.
- **A persisted last-known battery cache** to smooth Unknown flaps — deferred; would be an explicit
  component if added.

## Further Notes

### Spike first — gating artifact
The **first build artifact is a console enumeration spike**, and the whole design is gated on it.
Before running it: **connect the Bluetooth earphones (and any mouse/keyboard that matters)** — the
spike only sees what is connected at that moment. The spike must resolve, in one pass:

1. Which of `Kind.Device` / `DeviceContainer` / `AssociationEndpoint` carries the battery DEVPKEY.
2. The exact battery DEVPKEY string (confirm/replace the candidate).
3. Whether one query yields **both** battery and a reliable `Connected` flag, or two correlated by
   container id are needed.
4. That **absent ≠ 0** holds, and the runtime type of the property value.
5. The correct connection-status property/key.
6. Whether the **`bluetooth` app capability** is required (and re-check **packaged**, not just
   console).
7. Whether coordinated earbuds expose **per-member** battery (informs the deferred L/R feature).
8. Re-run the same provider code **inside the packaged extension process** to catch
   activation/lifetime differences from the console.

If the spike lights up the user's devices, build `Abstractions` → `Windows` → `Extension` TDD-style.
If it does **not** populate the property for the user's devices, **re-plan the data source before
writing extension code** — that is the entire point of spiking first.

### Cross-model review
The two highest-leverage decisions in this PRD — **device-property vs GATT** and the
**architecture/contract** — were reviewed with a second model (Codex) during design. Key
adjustments adopted from that review: bare invalidation event (not pushed snapshots); the
`RefreshCoordinator` as a named component; idempotent lifecycle; container-id aggregation across
child devnodes; splitting a pure `net8.0` abstractions library from the Windows provider.

### Known risks
- **Refresh/watcher timing** (overlap, event storms, disposal races) is the biggest real risk —
  mitigated by the `RefreshCoordinator`. Bigger than MSIX packaging.
- **CmdPal out-of-process lifetime** governs whether the `DeviceWatcher` can stay resident or the
  design is effectively snapshot-on-open; confirmed during first integration.
- **Container-id rotation** on firmware reset / LE address change could orphan future persisted
  per-device settings.
- ARM64 build configuration must exist for **every** project; only `BtBattery.Extension` is packaged.
