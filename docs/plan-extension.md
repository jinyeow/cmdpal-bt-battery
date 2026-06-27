# BtBattery.Extension — MVP Implementation Plan

> Status: Phase 1–3 complete. Pending: Phase 4 (dock band), Phase 5 (dock wiring), Phase 6 (ADRs).
> CLSID: `ae2698e1-5166-4448-9582-bdfdf457d95f`

## Acceptance bar

1. `dotnet build BtBattery.sln -p:Platform=x64` exits 0.
2. `Add-AppxPackage -Register <output>\AppxManifest.xml` registers without error.
3. CmdPal shows "Bluetooth Battery" in top-level commands.
4. Dock band shows lowest Known battery level (or "—" when all Unknown).
5. Flyout lists all connected BT devices with category icon + battery tag.

## Deviations from PRD already decided

- **RefreshCoordinator lives in `BtBattery.Abstractions`**, not the Extension layer (WinUI-free, testable seam). PRD will be updated.
- **No `IDockBandFactory` seam in v1** — single IContentPage flyout is the only dock layout; the factory is YAGNI until a second layout is added.

## Key decisions (see ADRs)

| Decision | Choice | Reason |
|---|---|---|
| COM server library | `Shmuelie.WinRTServer` | Matches EverythingCommandPalette (known-working open-source reference) |
| Extension TFM | `net9.0-windows10.0.22621.0` | CmdPal SDK compiled against SDK.NET 10.0.26100.38; net9.0 + `WindowsSdkPackageVersion=10.0.26100.38` avoids NETSDK1148 |
| RaiseItemsChanged thread | Direct call, no DispatcherQueue | Extension COM server has no WinUI UI thread; COM proxy marshals the WinRT event. Verify at runtime. |
| Dock band surface | `IContentPage` (expandable flyout) | PRD §149 / CmdPal dock band model |
| IDockBandFactory | Skipped (YAGNI) | PRD §159 describes it as "additive change later" — not in v1 |
| coordinator.Start() timing | Lazy (first TopLevelCommands() call) | Watcher-start failure in ctor blocks extension activation (Codex HIGH); lazy + catch makes it non-fatal |
| Watcher-start failure | Catch + log, continue without live updates | Device enumeration on-open still works; live watcher is a "nice-to-have" that degrades gracefully |
| bluetooth capability | Add `<DeviceCapability Name="bluetooth"/>` | Packaged WinRT BT enumeration may require it (spike unknown #6); adding it is safe, omitting is a runtime risk |
| NotifySummaryChanged exceptions | Caught in publish delegate | COM/WinRT exception from RaiseItemsChanged must not escape RefreshNowAsync (Codex MEDIUM) |
| GetProvider() caching | Field initializer in BtBatteryExtension | Repeated calls must not create multiple coordinators/watchers (Codex MEDIUM) |

## Phase 1 — Tracer bullet (scaffolding + loads in CmdPal) ✅

**Goal**: build cleanly, register, and show a placeholder item in CmdPal. No battery data yet.

Files to create:
```
src/BtBattery.Extension/
  BtBattery.Extension.csproj
  app.manifest
  Package.appxmanifest
  Program.cs
  BtBatteryExtension.cs         ← IExtension, returns CommandsProvider
  BtBatteryCommandsProvider.cs  ← CommandProvider, placeholder TopLevelCommands()
  Assets/                       ← placeholder icons (copy from Windows default)
```

Update `BtBattery.sln`: add Extension project under `src` folder, x64 + ARM64 configurations.

**NuGet packages** (Extension.csproj):
- `Microsoft.CommandPalette.Extensions` (0.9.260303001) — Toolkit.dll is bundled inside; no separate Toolkit package
- `Microsoft.WindowsAppSDK` (1.6.250228001)
- `Shmuelie.WinRTServer` (1.0.0) — `RegisterClass<T,TInterface>(ComServer)` API (no factory lambda in v1.0.0)
- Project ref: `BtBattery.Windows`

Key Phase 1 implementation constraints (Codex HIGH findings):
- `TopLevelCommands()` must return a real `ListItem` (wrapping `BtBatteryListPage`) — empty array means CmdPal shows nothing and the load path cannot be verified
- `_coordinator.Start()` must NOT be called in the ctor — call it lazily in the first `TopLevelCommands()` call, wrapped in try-catch; watcher-start failure is non-fatal (enumeration still works on-open)
- `Package.appxmanifest` must include `<DeviceCapability Name="bluetooth"/>` (spike unknown #6 resolved conservatively)

**Verify**: `dotnet build -p:Platform=x64` green ✅ + `Add-AppxPackage -Register` ✅ + CmdPal shows "Bluetooth Battery" (user verification needed).

No unit tests (Extension glue is deployment-verified per PRD §25–28 rationale).

## Phase 2 — Category glyph helper (TDD) ✅

**Goal**: map `DeviceCategory` → Segoe MDL2 Assets glyph string.

Files:
- `tests/BtBattery.Tests/DeviceCategoryGlyphTests.cs` (RED first)
- `src/BtBattery.Abstractions/DeviceCategoryGlyph.cs` (GREEN)

Glyphs (Segoe MDL2):
| Category | Glyph | Code |
|---|---|---|
| Headset | `` | Headphone |
| Mouse | `` | Mouse |
| Keyboard | `` | Keyboard |
| Other | `` | Bluetooth |

**Verify**: `dotnet test tests/BtBattery.Tests -p:Platform=x64` green.

## Phase 3 — List page ✅

**Goal**: `BtBatteryListPage` shows device rows from `BatterySummary`.

File: `src/BtBattery.Extension/Pages/BtBatteryListPage.cs`
- Extends `DynamicListPage` (or `ListPage`)
- `GetItems()`: triggers `_coordinator.RefreshNowAsync().GetAwaiter().GetResult()` (or async equivalent) then maps `_coordinator.Current.Rows` → `ListItem[]` (Codex MEDIUM: "on page open" = inside `GetItems()` override)
- Each row: category glyph icon + display name + battery tag (`{percent}%` colored, "—" for Unknown)
- `NotifySummaryChanged(BatterySummary)`: called from publish delegate; calls `RaiseItemsChanged(0)`

**Verify**: deployment test — open CmdPal, navigate to "Bluetooth Battery", verify rows appear.

## Phase 4 — Dock band (IContentPage)

**Goal**: dock flyout shows the summary.

File: `src/BtBattery.Extension/Pages/BtBatteryDockPage.cs`
- Extends `ContentPage` 
- `Title` = `_coordinator.Current.DockTitle` (updated on each publish)
- Content: embedded `BtBatteryListPage` (the flyout body is the same list)
- `BtBatteryCommandsProvider.GetDockBands()` override returns this page wrapped appropriately

**Verify**: dock band visible in CmdPal toolbar; click opens flyout with device list.

## Phase 5 — RefreshCoordinator wiring

**Goal**: live data flows through the extension.

In `BtBatteryCommandsProvider`:
```csharp
// ctor — coordinator.Start() is NOT called here (Codex HIGH: watcher failure = activation failure)
_provider = new DeviceInformationBatteryProvider();
_coordinator = new RefreshCoordinator(
    _provider,
    lowThreshold: 20,
    TimeProvider.System,
    publish: OnSummaryPublished,
    debounceWindow: TimeSpan.FromMilliseconds(250),
    fallbackInterval: TimeSpan.FromMinutes(5));

// lazy start — called from TopLevelCommands() on first access
private void EnsureStarted()
{
    if (_started) return;
    _started = true;
    try { _coordinator.Start(); }
    catch { /* watcher-start failure is non-fatal; on-open refresh still works */ }
}

// exceptions from RaiseItemsChanged must not escape the publish delegate (Codex MEDIUM)
void OnSummaryPublished(BatterySummary summary)
{
    try { _listPage.NotifySummaryChanged(summary); } catch { }
    try { _dockPage.NotifySummaryChanged(summary); } catch { }
}
```

`NotifySummaryChanged(BatterySummary)` on each page → updates `Title`/items → calls `RaiseItemsChanged(0)`.

Dispose chain: `BtBatteryExtension.Dispose()` → `BtBatteryCommandsProvider.Dispose()` → `_coordinator.Dispose()` → `_provider.Dispose()`.

**Verify**: dock band updates when BT device connects/disconnects; list page refreshes on open.

## Phase 6 — ADRs and PRD update

- Write `docs/adr/001-refresh-coordinator-location.md`
- Write `docs/adr/002-com-server-library.md`
- Write `docs/adr/003-dock-band-surface.md`
- Write `docs/adr/004-marshaling-strategy.md`
- Update `docs/PRD.md`: note RefreshCoordinator deviation, no IDockBandFactory in v1

## Out of scope for this plan

- Spike unknowns #6 (bluetooth capability) and #8 (provider in packaged process) — resolved by deploy in Phase 1
- ARM64 explicit platform config — add after Phase 1 confirms x64 works
- Low-battery visual emphasis (color tags) — deferred to follow-on

## Build + deploy commands

```powershell
# Build full solution (sln maps Any CPU → x64 per project; Extension csproj also takes -r win-x64 when building alone)
dotnet build BtBattery.sln

# Loose-file register (dev)  -- AppendTargetFrameworkToOutputPath=false, so no TFM subdirectory
$out = "src\BtBattery.Extension\bin\x64\Debug"
Get-Process -Name "BtBattery.Extension" -ErrorAction SilentlyContinue | Stop-Process -Force
Add-AppxPackage -Register "$out\AppxManifest.xml" -ForceApplicationShutdown

# Unregister
Get-AppxPackage -Name "JustinPuah.BtBattery" | Remove-AppxPackage
```
