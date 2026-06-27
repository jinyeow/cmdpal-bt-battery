# ADR 001 — RefreshCoordinator in BtBattery.Abstractions

**Status**: Accepted

## Context

The PRD described RefreshCoordinator as part of the Extension layer. During implementation
it was moved to `BtBattery.Abstractions` so it could be tested without a WinUI or COM
dependency. The Extension layer has no test project (deployment-verified per PRD §25–28).

## Decision

`RefreshCoordinator` lives in `BtBattery.Abstractions` alongside `BatterySummary` and
`IBatteryProvider`, not in `BtBattery.Extension`.

## Consequences

- `RefreshCoordinator` is fully unit-testable (38 tests, including debounce and coalesce).
- `BtBattery.Extension` depends on `BtBattery.Abstractions` via project reference.
- PRD §X needs updating to reflect the location change.
