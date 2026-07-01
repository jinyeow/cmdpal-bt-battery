# Bluetooth Battery — PowerToys Command Palette extension

A [PowerToys Command Palette](https://learn.microsoft.com/en-us/windows/powertoys/command-palette/overview)
extension that shows the battery levels of your connected Bluetooth devices (earbuds, mouse,
keyboard) right where you already work: the palette itself, and a persistent Dock band.

It reads the same cached battery property Windows Settings → Bluetooth & devices shows, so the
number never disagrees with the system, needs no active GATT session or pairing dance, and covers
both Classic (BR/EDR) and LE devices.

## Features

- **Dock band** headlining the lowest battery across your connected devices, with a `+N` suffix
  when more than one device is low. Emphasized when any device is at/below the low threshold.
- **Searchable list page** ("Bluetooth Battery") with every connected device and its level,
  reachable even if the Dock band isn't pinned.
- Devices whose battery Windows can't report are shown honestly as **Unknown**, never a fake 0%.
- Multi-endpoint devices (e.g. a dual-mode Classic+LE headset, or earbuds with separate L/R/case
  battery reports) collapse into a single row.
- Silent when Bluetooth is off or nothing is connected — no empty/placeholder noise.
- Refreshes on open, on device connect/disconnect/update, and on a 5-minute fallback timer.

## Requirements

- Windows 11
- [PowerToys](https://github.com/microsoft/PowerToys) with Command Palette enabled
- .NET 9 SDK (pinned via `global.json`) to build

## Install

### From a GitHub Release

Grab the `.msix` for your architecture (x64 or ARM64) and its matching `.cer` from the
[latest release](../../releases/latest). Each release is signed with a throwaway certificate
that isn't shared across releases (see [ADR 005](docs/adr/005-release-signing.md)), so trust the
`.cer` before every install/upgrade (substitute `x64`/`ARM64` for your arch):

```powershell
# One-time trust for this release (elevated PowerShell)
Import-Certificate -FilePath .\BtBattery-<arch>.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople

# Install
Add-AppxPackage -Path .\BtBattery_<version>_<arch>.msix
```

### Development sideload

Build and sideload locally instead:

```powershell
# Build
dotnet build src/BtBattery.Extension/BtBattery.Extension.csproj -c Debug -p:Platform=x64

# Stop CmdPal and any running instance of the extension first (file locks)
Get-Process -Name "Microsoft.CmdPal.UI","BtBattery.Extension" -ErrorAction SilentlyContinue | Stop-Process -Force

# Register the MSIX package
Get-AppxPackage -Name "JustinPuah.BtBattery" -ErrorAction SilentlyContinue | Remove-AppxPackage
Add-AppxPackage -Register "src\BtBattery.Extension\bin\x64\Debug\AppxManifest.xml"

# Launch (or just re-open) Command Palette
Start-Process "shell:AppsFolder\Microsoft.CommandPalette_8wekyb3d8bbwe!App"
```

After the first install, search the palette for "Bluetooth Battery" to use the list page, or pin
the Dock band from CmdPal's Settings → Bands.

## Development

See [`CLAUDE.md`](CLAUDE.md) for the full architecture writeup (project layering, the
`IBatteryProvider` seam, the pure `BatterySummary.Compute` display logic, `RefreshCoordinator`) and
build/test commands. Product decisions and rationale live in [`docs/PRD.md`](docs/PRD.md) and
[`docs/adr/`](docs/adr/).

Quick reference:

```powershell
# Build
dotnet build src/BtBattery.Extension/BtBattery.Extension.csproj -c Debug -p:Platform=x64

# Test
dotnet test tests/BtBattery.Tests/ -p:Platform=x64
```

## Project layout

```
src/
  BtBattery.Abstractions/   domain model, IBatteryProvider, BatterySummary.Compute, RefreshCoordinator
  BtBattery.Windows/        DeviceInformationBatteryProvider — the one OS-touching class
  BtBattery.Extension/      MSIX-packaged CmdPal COM server (glue only, no business logic)
  BtBattery.Spike/          console repro for the Bluetooth enumeration query, not shipped
tests/
  BtBattery.Tests/          xUnit tests over BtBattery.Abstractions
docs/
  PRD.md                    product spec, contracts, out-of-scope
  adr/                      decisions where the implementation diverged from the PRD
  plan-extension.md         phased implementation plan
```

## Status

v1 MVP complete: connected-device battery levels, Dock band, searchable list page, low-battery
emphasis, background refresh. See `docs/PRD.md`'s "Out of Scope" section for what's deliberately
not here yet (GATT fallback, per-device customization, disconnected-device views, notifications).

## License

[MIT](LICENSE)
