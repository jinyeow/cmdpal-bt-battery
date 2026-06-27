# ADR 004 — WinRT Marshaling: Direct Calls, No DispatcherQueue

**Status**: Accepted

## Context

CmdPal calls into the extension over a COM proxy. The extension COM server runs in an MTA
thread (`[MTAThread]` on `Main`). There is no WinUI or CoreWindow — no DispatcherQueue.

When `RefreshCoordinator` publishes a summary, the publish delegate runs on a thread pool
thread. That delegate calls `_listPage.NotifySummaryChanged(summary)` which calls
`RaiseItemsChanged(0)`. The question is whether `RaiseItemsChanged` must be called on a
specific thread.

## Decision

Call `RaiseItemsChanged(0)` directly from the publish delegate (thread pool thread). The
WinRT event is COM-marshaled by the proxy to CmdPal's apartment. No DispatcherQueue is
needed because:

1. The extension has no UI thread.
2. WinRT COM proxies handle cross-apartment marshaling transparently for events.
3. EverythingCommandPalette follows the same pattern without DispatcherQueue.

Exceptions from `RaiseItemsChanged` are caught in the publish delegate to prevent them
from escaping into `RefreshCoordinator`.

## Consequences

- Simpler code — no DispatcherQueue setup, no `Microsoft.UI.Dispatching` dependency.
- If CmdPal has a strict same-thread requirement for `RaiseItemsChanged` we would see
  an access-violation at runtime; but the COM proxy architecture makes this unlikely.
- `partial` is required on all WinRT-crossing types (CsWinRT1028) for AOT/trimming safety.
