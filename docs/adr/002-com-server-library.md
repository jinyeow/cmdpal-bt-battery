# ADR 002 — COM Server Library: Shmuelie.WinRTServer

**Status**: Accepted

## Context

CmdPal extensions run as out-of-process COM servers. The extension must register a CLSID
via the package activation catalog and respond to `CoCreateInstance`. Several library options
exist:

- Roll our own (`CoRegisterClassObject` P/Invoke)
- `Shmuelie.WinRTServer` (open-source, used by EverythingCommandPalette)
- WinUI3 hosting shim (overkill for a headless COM server)

## Decision

Use `Shmuelie.WinRTServer` 1.0.0. EverythingCommandPalette is an actively-maintained
open-source CmdPal extension that uses the same library and same CLSID pattern, making it
a reliable working reference.

## Consequences

- `RegisterClass<T, TInterface>(ComServer)` in v1.0.0 takes no factory lambda; `T` must
  have a parameterless constructor. Shutdown signaling uses a static `ManualResetEvent` on
  the extension class (a singleton per process lifetime).
- The older API used in some examples (`server.RegisterClass<T,I>(() => instance)`) does
  not exist in v1.0.0 — the lambda overload was removed.
