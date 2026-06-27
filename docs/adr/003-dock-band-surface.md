# ADR 003 — Dock Band Surface: WrappedDockItem + ListItem

**Status**: Accepted

## Context

The PRD described the dock band as a `ContentPage` (expandable flyout). The actual
CmdPal SDK dock band API is:

- `ICommandProvider3.GetDockBands()` returns `ICommandItem[]`
- `ICommandItem.Command` type determines rendering:
  - `IContentPage` → expandable flyout button
  - `IListPage` → all items rendered as individual buttons in one band
  - `IInvokableCommand` → single button
- `WrappedDockItem(IListItem[], id, displayTitle)` creates a band backed by an internal
  `ListPage` containing the provided items — each item renders as one button

## Decision

Use `WrappedDockItem([_dockItem], "BtBattery.dock", "Bluetooth Battery")` where `_dockItem`
is a `ListItem` whose `Command` is `BtBatteryListPage`. This gives:
- One dock button showing `Title="Bluetooth Battery"` and `Subtitle=BatterySummary.DockTitle`
- Clicking navigates into `BtBatteryListPage` showing all connected devices

No separate `BtBatteryDockPage.cs` file needed — the existing list page serves as both
the top-level command and the dock flyout content.

## Consequences

- `GetDockBands()` is a virtual override on `CommandProvider` (via `ICommandProvider3`).
- `_dockItem.Subtitle` is updated in `OnSummaryPublished` to reflect live battery data.
- No `IDockBandFactory` abstraction in v1 — YAGNI; factory is additive only.
