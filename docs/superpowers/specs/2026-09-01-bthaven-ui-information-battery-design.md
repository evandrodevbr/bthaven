# BTHaven UI Information, Selection, and Battery Design

**Status:** proposed for review after approval of selection policy A on 2026-09-01.

## Objective

Reorganize the WinUI 3 surface so a user can identify the active device, read trustworthy connection/battery telemetry, and reach the relevant action without scanning one long diagnostic page. The change also makes list selection deterministic across refreshes, filters, watcher events, removals, and app restarts while preserving the existing Fluent/Mica identity.

The approved selection rule is **policy A**:

> Keep the last explicitly selected logical device while it still exists and is visible, even when disconnected. If no valid selection exists, display the first item in the visible list.

## Scope

This design covers five independently testable capabilities:

| Module | Responsibility | Depends on |
|---|---|---|
| `endpoint-preference` | Preserve endpoint observation state and select the connected endpoint inside a logical aggregate | Existing logical identity/merge |
| `selection-policy` | Resolve current, persisted, and fallback selection without transient clears or background jumps | Existing logical device ID |
| `battery-telemetry` | Hydrate and cache honest per-device battery readings without aggressive polling or overwriting watcher truth | `IBatteryService`, selection priority |
| `information-architecture` | Present overview, audio, and diagnostics in an Operate-oriented Fluent hierarchy | Selection and telemetry states |
| `adaptive-command-surface` | Keep filter/actions reachable at wide, snapped, and compact widths | Information architecture |

Dependency direction is one-way:

```text
Bluetooth observations
        │
        ├── endpoint-preference ──> logical device projection
        │                              │
        └──────────────────────────────┼──> selection-policy
                                       │          │
IBatteryService ──> battery-telemetry ─┘          │
                                                  ▼
                          WinUI rows + selected-device surface
                                                  │
                                 adaptive Fluent command/layout states
```

## Existing evidence and defects

- `MainPage.xaml.cs:466-506` clears and rebuilds `Rows`. `Rows.Clear()` transiently clears `ListView.SelectedItem`, so `DeviceList_SelectionChanged` at `MainPage.xaml.cs:508-516` erases `selectedDeviceId` before the restoration branch runs.
- `RefreshSelectedDeviceCapabilitiesAsync` writes `device with { Battery = battery }` back at `MainPage.xaml.cs:561-562`. A battery request using an older model can therefore overwrite newer watcher state for the same logical ID.
- Startup, filter changes, selected-device removal, and a nonempty list after an empty state do not select a fallback.
- `selectedDeviceId` is in-memory only; the stable logical ID is already available through `BluetoothDeviceModel.Id`.
- `BluetoothDeviceManager.Merge` at `BluetoothDeviceManager.cs:596-645` orders endpoint observations by transport/ID and takes the first scalar values, even if another observation is the connected endpoint.
- The header at `MainPage.xaml:25-44` combines a long title with an indivisible horizontal filter/button group. The body at `MainPage.xaml:47-153` has fixed `340,*` columns and no adaptive states.
- The selected surface is one long stack with global status inside its scroll viewer. Connection facts, actions, explanations, and raw diagnostics have nearly equal visual weight.
- List rows expose only battery percentage. The model already carries RSSI and observation time, while `BatteryState` carries percentage, charging, source, confidence, and update time.

## Architectural boundaries

1. `BTHaven.Core` remains UI-free and owns deterministic policies that operate only on domain records.
2. `BTHaven.Windows` remains the only layer that aggregates WinRT endpoint observations. Connected-endpoint preference is not duplicated in XAML code-behind.
3. `BTHaven.App` owns persistence, visible-list reconciliation, presentation state, and Fluent controls.
4. A battery result never replaces a `BluetoothDeviceModel` captured from the watcher. Watcher state and telemetry cache are separate sources joined only for rendering.
5. No timer or aggressive polling is added. DeviceWatcher events, initial/manual refresh, selection, and meaningful connection transitions trigger bounded work.
6. No new dependency or custom control library is introduced. Use Windows App SDK 2.4 controls and existing XamlControlsResources.

## Domain contracts

### Endpoint observation state

Extend the existing endpoint reference without changing its stable identity semantics:

```csharp
public sealed record BluetoothEndpointReference
{
    public required string Id { get; init; }
    public required BluetoothTransport Transport { get; init; }
    public string? ContainerId { get; init; }
    public string? Address { get; init; }
    public bool? IsConnected { get; init; }
    public bool? IsPresent { get; init; }
    public DateTimeOffset ObservedAt { get; init; }
}
```

Add one canonical selector:

```csharp
public static BluetoothEndpointReference? SelectPreferredConnection(
    BluetoothDeviceModel device);
```

Priority is deterministic:

1. `IsConnected == true`;
2. `IsPresent == true`;
3. most recent `ObservedAt`;
4. transport enum value;
5. endpoint ID, ordinal-ignore-case.

`BluetoothDeviceManager.Merge` uses the same ordering for scalar provenance. `IsConnected`, `IsPresent`, capabilities, services, profiles, and aggregate `Transport` keep their current aggregate semantics. Name, manufacturer, model, address, and category prefer the selected observation and fall back through the same ordered observations only when the preferred value is absent.

When Classic and LE are both connected, the UI does not imply that only one exists: the preferred endpoint supplies the compact “current connection” summary, and the details view states that multiple endpoints are active.

### Selection policy

Add a pure policy with no WinUI types:

```csharp
public static class BluetoothDeviceSelectionPolicy
{
    public static string? Resolve(
        IReadOnlyList<BluetoothDeviceModel> visibleDevices,
        string? currentDeviceId,
        string? preferredDeviceId);
}
```

`visibleDevices` is already in the exact order displayed by the list. Resolution is:

1. current ID if it exists in `visibleDevices`;
2. persisted preferred ID if it exists in `visibleDevices`;
3. `visibleDevices[0].Id`;
4. `null` only when `visibleDevices` is empty.

Comparison is ordinal-ignore-case. Connection status never displaces a valid current/preferred device. The existing row order (`IsConnected` descending, then name) means the first fallback naturally prefers a connected item without violating policy A.

### Battery telemetry

Keep query status separate from the last trustworthy reading:

```csharp
public enum BatteryTelemetryStatus
{
    NotRequested,
    Loading,
    Available,
    Unavailable,
    Failed,
}

public sealed record BatteryTelemetryEntry
{
    public required string DeviceId { get; init; }
    public BatteryTelemetryStatus Status { get; init; }
    public BatteryState? Current { get; init; }
    public BatteryState? LastAvailable { get; init; }
    public DateTimeOffset AttemptedAt { get; init; }
}

public sealed class BatteryTelemetryChangedEventArgs : EventArgs
{
    public required BatteryTelemetryEntry Entry { get; init; }
}

public sealed class BatteryTelemetryCoordinator
{
    public BatteryTelemetryCoordinator(IBatteryService service, int maxConcurrency = 2);

    public event EventHandler<BatteryTelemetryChangedEventArgs>? Changed;

    public bool TryGet(string deviceId, out BatteryTelemetryEntry entry);

    public Task RefreshAsync(
        IReadOnlyList<BluetoothDeviceModel> devices,
        string? priorityDeviceId,
        CancellationToken cancellationToken = default);

    public void Prune(IReadOnlySet<string> validDeviceIds);
}
```

The coordinator:

- queries the selected/priority device first;
- queries only present devices, with maximum concurrency 2;
- runs on initial enumeration, manual refresh, device addition, transition to connected, endpoint-set change, and explicit selection when no current sample exists;
- does not run periodically and does not requery on every watcher property update;
- uses a per-device generation so a late result cannot replace a newer attempt;
- retains `LastAvailable` when a later attempt is unavailable/failed, and labels it as a previous reading rather than current;
- raises `Changed` without touching WinUI; MainPage marshals the event through DispatcherQueue before updating UI-bound collections;
- prunes only IDs no longer known to the device manager, not devices merely hidden by a filter.

`BluetoothDeviceModel.Battery` remains compatible for inspectors and existing callers, but MainPage no longer mutates the device dictionary to attach a battery response. Row/detail rendering joins the device model with `BatteryTelemetryEntry`.

## Selection and update state machine

### State

| State | Meaning |
|---|---|
| `preferredDeviceId` | Last explicit user selection loaded/saved under `ApplicationData.Current.LocalSettings["PreferredBluetoothDeviceId"]` |
| `selectedDeviceId` | Logical ID currently rendered and selected in the visible list |
| `selectionEpoch` | Monotonic counter incremented only when logical selection changes or is invalidated |
| `isReconcilingSelection` | Suppresses transient `SelectionChanged` while rows are synchronized |
| `selectedInspection` | Snapshot tied to both logical ID and the observation/selection epoch |

### Transitions

| Event | Resolution | Persist? | Async effect |
|---|---|---|---|
| App load | current is null; restore preferred if visible, else first item | No write unless user later selects | Start selected device first, then visible battery hydration |
| User selects device | select it and set preferred | Yes, immediately | Increment epoch; cancel/ignore prior capability work |
| Watcher updates selected ID | preserve selection and row identity | No | Merge current model; refresh only connection-dependent data when endpoint/connection changed |
| Watcher adds another device | preserve valid current selection | No | Hydrate new present row; never steal focus |
| Partial endpoint removal | logical ID remains; preserve selection | No | Recompute preferred endpoint; invalidate stale endpoint-dependent inspection |
| Last endpoint removal | current becomes invalid; preferred remains stored; choose first visible item | No | Increment epoch; start fallback capabilities |
| Filter change | keep current if visible; else preferred if visible; else first visible | No | Increment only if resolved ID changes |
| Manual refresh | preserve current/preferred by ID across row synchronization | No | New telemetry generation; selected first |
| Visible list becomes empty | selected null; show empty state; keep preferred | No | Cancel/ignore selected capability work |
| First item appears after empty | choose first visible item | No | Start capabilities once |
| Selected device disconnects | preserve while it exists and is visible | No | Update status; do not jump to another connected device |

`ClearSelection` is called only when the resolved ID is actually null. A programmatic transient from collection mutation is not interpreted as user intent.

All selected-device async operations capture `(deviceId, selectionEpoch)`. Before each UI write they require both values to match current state. Capability queries resolve the latest model from `devices`; battery results remain in the telemetry cache and never copy captured connection fields back into watcher state.

## Information architecture

The surface remains a Fluent/Mica desktop tool, not a dashboard redesign.

### Window shell

Keep the existing `MainWindow` Mica backdrop, TitleBar control, tray behavior, and title-bar row. Page commands remain below the drag region.

### Page hierarchy

```text
Page
├── Product header
│   ├── Bluetooth Hub title/subtitle
│   └── Global CommandBar
│       ├── Logs
│       └── Diagnostics
├── Persistent StatusInfoBar
└── Adaptive master/detail region
    ├── Device pane
    │   ├── “Dispositivos” + count
    │   ├── Filter ComboBox
    │   ├── Refresh AppBarButton
    │   └── Device ListView
    └── Selected-device pane
        ├── Name + connection badge + last observed
        ├── SelectorBar: Resumo | Áudio | Diagnóstico
        └── Selected view
```

`Atualizar` moves beside the device filter because both act on inventory. `Logs` and `Diagnósticos` stay global and use a native `CommandBar`; diagnostic commands can enter dynamic overflow, while list refresh remains directly reachable.

### Selected-device views

1. **Resumo**
   - connection status, preferred active endpoint transport, address/container, RSSI, and observation time;
   - battery value/charging, current-vs-last reading, source, confidence, and battery observation time;
   - observed capabilities.
2. **Áudio**
   - A2DP target/action and auto-reconnect;
   - Windows default output endpoint and explanation;
   - remote smartphone volume;
   - HFP test/status, shown as not applicable for non-smartphones without occupying the primary summary.
3. **Diagnóstico**
   - inspection action/status;
   - monospaced inspection result and evidence boundary.

Use `SelectorBar` from Windows App SDK for the three stable views. The selected view is preserved while switching devices unless the view is not applicable; non-applicable actions remain explained and disabled rather than disappearing unpredictably.

### Device rows and telemetry

Each row has three reading levels:

- name + device icon;
- category/aggregate transport + connection status;
- compact telemetry: battery/charging, RSSI when exposed, and last observation time.

Rules:

- `68%` and a charging glyph/text when current;
- `Última bateria: 68% · 21:42` after a current attempt is unavailable but a prior trustworthy sample exists;
- `Bateria indisponível` only in accessible text/detail; the compact visual may use `—`;
- RSSI is rendered as `−42 dBm`; never convert it to invented “signal bars” or quality labels;
- source/confidence remain in Resumo, not crowded into the row;
- timestamps are explicit local times; no per-second timer is introduced.

## Responsive topology

Use `VisualStateManager`/`AdaptiveTrigger`, not manual window-size event handlers.

| State | Width | Header | Body |
|---|---:|---|---|
| Wide | `>= 1100` effective px | title and global CommandBar in one row | device pane `320` and flexible detail |
| Medium | `720-1099` | title then CommandBar on a second row | device pane `280` and flexible detail |
| Compact | `< 720` | stacked header; CommandBar overflow | one-pane `SplitView`: list or selected detail, with native Back command |

Exact thresholds may move only after runtime inspection at 100%, 150%, and 200% text scaling; the topology and behavior do not change. Do not solve clipping by imposing a large minimum window width.

## Fluent visual and accessibility contract

- Preserve Mica, 12 px corner radius, semibold headings, existing product copy tone, and Fluent native controls.
- Replace `#0DFFFFFF`/`#1EFFFFFF` page brushes with semantic `ThemeResource` card/background/stroke resources so light, dark, and high-contrast modes remain legible.
- Use spacing tokens consistently: 8 within a group, 12 between related controls, 20/24 between sections/panes.
- Give the filter an accessible name/header.
- Expose each media toggle as `Áudio de mídia para {device name}`; do not reuse one indistinguishable automation name.
- Set heading levels for page, pane, and selected-view headings.
- Reading/focus order follows visual order. Reconciliation restores focus to the same logical row; compact navigation moves focus to the selected-device heading and Back returns focus to the originating row.
- Every icon-only AppBarButton has Label and AutomationProperties.Name. Connection and battery states are never conveyed by color/glyph alone.
- Support keyboard navigation, visible focus, Escape/Back in compact detail, and F5 for refresh. Do not add custom keyboard handling where CommandBar/ListView already provides it.
- Text must remain usable at 200% scaling without overlap; labels wrap before values are truncated.

## Exact file impact

### Create

- `src/BTHaven.Core/Devices/BluetoothDeviceSelectionPolicy.cs` — pure approved selection policy.
- `src/BTHaven.Core/Battery/BatteryTelemetryCoordinator.cs` — bounded query/cache/generation coordinator.
- `src/BTHaven.App/MainPage.Selection.cs` — persistence and atomic list/selection reconciliation.
- `src/BTHaven.App/MainPage.Telemetry.cs` — coordinator event handling and selected/list telemetry rendering.
- `tests/BTHaven.Core.Tests/BluetoothDeviceSelectionPolicyTests.cs` — policy A state table.
- `tests/BTHaven.Core.Tests/BatteryTelemetryCoordinatorTests.cs` — bounded ordering, stale generation, last-known and prune behavior.

### Modify

- `src/BTHaven.Core/Devices/BluetoothDeviceModel.cs` — endpoint observation state and preferred endpoint selector.
- `src/BTHaven.Core/Devices/BluetoothDeviceProjection.cs` — populate endpoint state.
- `src/BTHaven.Windows/Bluetooth/BluetoothDeviceManager.cs` — connected-first scalar provenance and endpoint state.
- `src/BTHaven.App/MainPage.xaml` — command scopes, persistent status, SelectorBar views, adaptive states, accessibility metadata, theme resources.
- `src/BTHaven.App/MainPage.xaml.cs` — delegate reconciliation/telemetry, epoch-guard capability queries, stop mutating watcher models with battery data.
- `src/BTHaven.App/MainPage.Actions.cs` — preserve selection around row refreshes triggered by A2DP actions and use current epoch/device guards.
- `src/BTHaven.App/DeviceRowViewModel.cs` — current/last-known battery, RSSI, observation time, connected endpoint and accessible telemetry text.
- `tests/BTHaven.Core.Tests/BluetoothDeviceModelTests.cs` — preferred endpoint ordering.
- `tests/BTHaven.Core.Tests/BluetoothDeviceProjectionTests.cs` — endpoint state projection.
- `tests/BTHaven.IntegrationTests/BluetoothDeviceManagerTests.cs` — connected observation scalar provenance and multiple-connected deterministic tie.

### Explicitly unchanged

- `README.md`.
- `docs/implementation-correction-spec.md`.
- `src/BTHaven.App/MainWindow.xaml` and `.xaml.cs` unless runtime verification disproves the existing shell separation.
- A2DP/HFP service contracts, package capabilities, tray lifecycle, and diagnostics redaction.

## Acceptance criteria

### Selection

- A user-selected visible logical device remains selected through manual refresh, watcher update, battery result, A2DP state change, list reorder, and disconnection.
- Restart restores the last explicit logical ID when it is visible.
- If current is invalid, persisted preferred is used when visible; otherwise the first visible row is selected.
- Filter/removal/empty transitions follow the state table and never show one row selected while another device's details/actions are rendered.
- Partial endpoint removal preserves the logical row and selection.
- Late capability/battery results for a prior epoch cannot update the current device surface.

### Endpoint preference

- A logical aggregate with one connected and one disconnected endpoint uses the connected observation for compact connection fields.
- Multiple connected endpoints resolve deterministically and are disclosed as multiple active connections.
- Aggregate paired/present/connected/capability semantics remain unchanged.

### Battery and telemetry

- Every present visible row reaches Available, Unavailable, or Failed after bounded hydration; selected device is attempted first.
- No more than two battery provider calls run concurrently.
- A late result cannot overwrite a newer generation.
- A prior available reading survives a transient unavailable/failed attempt and is visibly labeled as last known.
- RSSI and timestamps are shown only when present and retain their actual units/source semantics.
- No periodic polling is introduced.

### UI and accessibility

- Global commands, filter, refresh, list, and selected-device content remain reachable in Wide, Medium, and Compact states.
- At 200% text scale, controls do not overlap; compact state provides a complete keyboard-accessible list/detail path.
- The system status remains visible while scrolling selected-device content.
- Light, dark, and high-contrast modes use semantic Fluent resources.
- NVDA/Accessibility Insights can distinguish each device media toggle and announce selection/status changes.

## Risks and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Collection rebuild still fires transient selection events | Wrong details/actions | Suppress reconciliation events and resolve ID before mutation; test policy independently and smoke-test ListView behavior |
| Connected endpoint flips rapidly | Visible metadata flicker | Stable deterministic priority; preserve logical selection; only rerender changed values |
| Battery queries delay refresh | Sluggish UI/device discovery | Decouple coordinator from inventory completion, selected-first ordering, max concurrency 2 |
| GATT unavailable after a prior reading | Misleading stale battery | Separate `Current` from `LastAvailable` and show observation time/last-known label |
| LocalSettings contains obsolete logical ID | Blank startup | Treat it as preferred only; first visible fallback always resolves |
| Compact SplitView loses focus | Keyboard/screen-reader regression | Explicit focus transfer and return target; verify with keyboard and AX tree |
| SelectorBar hides important state | Reduced awareness | Keep device name, connection badge, observation time, and global status outside the selected view |
| Scalar provenance change alters tests/correlation | Domain regression | Characterization tests before merge change; identity/capability aggregation remains untouched |

## Decisions locked for implementation

- Selection policy A is final for this work: valid visible selection wins even when disconnected.
- First-item fallback uses visible list order; connected-first ordering remains a list concern, not a selection override.
- Connected endpoint preference belongs to the aggregate/domain boundary, not code-behind.
- Battery telemetry is event/refresh driven with bounded queries; no polling timer.
- The recommended layout is master/detail with `SelectorBar`, CommandBar, persistent status, and compact SplitView.
- Fluent/Mica identity is refined, not replaced.

There are no remaining product decisions required before implementation. Runtime breakpoint tuning is an evidence-based implementation detail, not a new design decision.
