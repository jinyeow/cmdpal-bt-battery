# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A PowerToys **Command Palette (CmdPal)** extension that surfaces connected Bluetooth devices'
battery levels (earbuds, mouse, keyboard) inside the palette and in a persistent Dock band. It
reads the same cached PnP battery property that Windows Settings shows — no active GATT session,
no pairing dance. See `docs/PRD.md` for the full product spec and `docs/adr/*.md` for decisions
where the implementation diverged from it.

## Build / test / run

```powershell
# Build the extension (always specify Platform=x64 or ARM64 — there is no AnyCPU)
dotnet build src/BtBattery.Extension/BtBattery.Extension.csproj -c Debug -p:Platform=x64

# Run all tests
dotnet test tests/BtBattery.Tests/ -p:Platform=x64

# Run a single test
dotnet test tests/BtBattery.Tests/ -p:Platform=x64 --filter "FullyQualifiedName~RefreshCoordinatorTests.RefreshNow_SerializesOverlappingRefreshes"

# Kill CmdPal + the extension host before rebuilding (file locks otherwise block the build)
Get-Process | Where-Object { $_.Name -like "*CmdPal*" -or $_.Name -like "*BtBattery*" } | Stop-Process -Force

# Restart CmdPal to pick up a fresh build
Start-Process "shell:AppsFolder\Microsoft.CommandPalette_8wekyb3d8bbwe!App"
```

There is no top-level solution build script — build `BtBattery.Extension.csproj` directly (it
pulls in `BtBattery.Windows` and `BtBattery.Abstractions` via project references). `BtBattery.Spike`
is a standalone console repro for the Bluetooth enumeration query and is not part of the extension
build.

Requires the **.NET 9 SDK** (pinned via `global.json`) even though most projects target
`net8.0`/`net8.0-windows10.0.19041.0` — only `BtBattery.Extension` targets `net9.0-windows10.0.22621.0`,
but the whole solution builds with whichever SDK `global.json` selects.

### Toolchain gotchas (don't "fix" these away)

`BtBattery.Extension.csproj` pins `WindowsSdkPackageVersion` and `CsWinRTWindowsMetadata`
explicitly, and depends on `Shmuelie.WinRTServer` **2.2.1+** with
`using Shmuelie.WinRTServer.CsWinRT;` in `Program.cs`. These aren't arbitrary: the Windows SDK
install on dev machines can be missing `UnionMetadata`, and `Shmuelie.WinRTServer` 1.x registers
the COM class factory without CsWinRT's `DefaultComWrappers`, which makes CmdPal's `QueryInterface`
for `IExtension` silently fail — the process starts but `GetProvider()` is never called and the
extension never shows up. If you touch `Program.cs` or bump that package, verify the extension
still appears in CmdPal's `commandProviderCache.json`, not just that it builds.

## Architecture

Five projects, layered strictly bottom-up (each only depends on the one below it):

```
BtBattery.Abstractions   net8.0, no WinRT/WinUI — domain model + pure logic + coordinator
        ^
BtBattery.Windows        net8.0-windows10.0.19041 — the one OS-dependent provider
        ^
BtBattery.Extension      net9.0-windows10.0.22621, packaged (MSIX) — CmdPal glue, no business logic
BtBattery.Spike          net8.0-windows10.0.19041, console — enumeration repro, reuses BtBattery.Windows
BtBattery.Tests          net8.0, xUnit — tests BtBattery.Abstractions only
```

`RefreshCoordinator` was deliberately moved into `BtBattery.Abstractions` (not `Extension`, despite
what the PRD originally said) so it's unit-testable without WinUI/COM — see ADR 001.
`BtBattery.Extension` has no test project; it's deployment-verified only (glue code, not logic).

### The seam: `IBatteryProvider`

`BtBattery.Windows.DeviceInformationBatteryProvider` is the only OS-touching class. It runs two
independent `DeviceWatcher`s against `DeviceInformation.FindAllAsync`/`CreateWatcher`:
- **`Kind.Device`** nodes (selector `BluetoothDeviceSelector`) carry the battery DEVPKEY and
  identity properties.
- **`Kind.DeviceContainer`** nodes (selector `ConnectedContainerSelector`) carry connection state
  and display name — connection state is *never* on the device node.

Both are queried/watched concurrently and correlated by `System.Devices.ContainerId` in
`Aggregate()`. Multiple child devnodes under one container (e.g. earbud L/R/case, or a dual-mode
Classic+LE device) collapse into a single `MonitoredDevice`, taking the **lowest** known battery
percent and setting `HasMultipleBatteryValues` when readings disagree. The local Bluetooth radio
adapter itself is excluded (`BTH\MS_*` instance-id prefix). See `BluetoothDeviceProperties.cs` for
the exact DEVPKEY/AQS strings — these were confirmed against real hardware by the spike and should
not be changed without re-spiking.

`DevicesInvalidated` carries no payload — it just means "re-query". It's contractually allowed to
fire on any thread and must never fire after `Dispose()`.

### The deep module: `BatterySummary.Compute`

`BatterySummary.Compute(devices, lowThreshold)` in `BtBattery.Abstractions` is pure and stateless —
the single place display rules live. Given a device snapshot it decides: the headline (lowest
*Known* battery; Unknown devices never headline), `LowCount` (devices at/below threshold), row sort
order (known-low → known-normal → Unknown-last), and the `StatusLine` string (`"{pct}% {name}"`,
`+{N-1}` suffix when 2+ devices are low). This is the function to change for any display-rule
change, and it's the thing to test directly — don't mock it.

### `RefreshCoordinator`: serializing three refresh triggers

Three things can ask for a refresh — the on-open call (`RefreshNowAsync`, awaited by pages so they
render fresh data), watcher invalidations (debounced — see `debounceWindow`), and a fallback timer
(`fallbackInterval`, currently 5 min, in case the watcher misses something). `RefreshCoordinator`
serializes all three through one semaphore so overlapping refreshes can't publish stale results out
of order, and coalesces "N requests while one is running" into "the running one + exactly one more".
It takes an injected `TimeProvider` (tested with `Microsoft.Extensions.Time.Testing.FakeTimeProvider`)
and a `publish` delegate so it stays WinUI-free — see ADR 004 for why no `DispatcherQueue` marshaling
is needed (the extension is `[MTAThread]`; WinRT COM proxies marshal events across apartments for
free).

### Extension layer: CmdPal glue

`BtBatteryCommandsProvider` wires a `RefreshCoordinator` to a `BtBatteryListPage` (the searchable
top-level page, `CommandIds.ListPage`) and a `WrappedDockItem` (the Dock band — see ADR 003 for why
it's `WrappedDockItem` wrapping a `ListItem[]`, not the `IContentPage` the PRD originally described).
`OnSummaryPublished` is the single place that pushes a new `BatterySummary` out to both surfaces; it
is guarded by `BatterySummary.ContentEquals` to stop `GetItems() → requestRefresh() →
OnSummaryPublished (unchanged) → NotifySummaryChanged() → GetItems()` from looping forever — **do
not remove that guard**. `CommandIds.ListPage` must stay in sync with the `CommandId` CmdPal has
persisted for this extension in its `settings.json`; changing it orphans existing pins.

`BtBatteryExtension` is the WinRT-activated `IExtension` singleton; `Program.cs` is the bare COM
server bootstrap (`Shmuelie.WinRTServer`), started with `-RegisterProcessAsComServer` and torn down
via a static `ManualResetEvent` set from `Dispose()`.

### Testing conventions

xUnit, assert on **observable output** (the returned `BatterySummary` / coordinator `Current`), not
on internal call sequences. `Compute` is tested directly since it's pure. `RefreshCoordinator` is
tested with a fake `IBatteryProvider` + `FakeTimeProvider` to control debounce/fallback timing
deterministically. `DeviceInformationBatteryProvider` (the OS-bound provider) is intentionally not
unit-tested — it's verified via `BtBattery.Spike` and an in-process packaged run instead.
