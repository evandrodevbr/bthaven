# BTHaven UI Information and Battery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a Fluent/Mica WinUI device surface with deterministic persisted selection policy A, connected-endpoint provenance, honest per-device battery/telemetry, reachable top commands, and adaptive information architecture.

**Architecture:** Add pure endpoint, selection, and telemetry policies below WinUI; keep watcher-owned device state separate from battery cache; reconcile the visible collection and selected logical ID atomically. Refine MainPage into an adaptive master/detail surface with native CommandBar, SelectorBar, SplitView, semantic theme resources, and accessible state labels.

**Tech Stack:** C# / .NET 10 / WinUI 3 / Windows App SDK 2.4 / WinRT Bluetooth / xUnit 2.9.3 / Windows 11 x64.

**Spec:** `docs/superpowers/specs/2026-09-01-bthaven-ui-information-battery-design.md`

## Global Constraints

- Approved policy A is immutable for this plan: a current visible logical device remains selected even when disconnected; otherwise use visible persisted preference, then the first visible item.
- `BluetoothDeviceModel.Id` remains the stable logical identity. Never use it as an endpoint ID.
- Connected endpoint preference is implemented once in Core/Windows aggregation, never duplicated in MainPage.
- DeviceWatcher remains the source of connection/presence truth; battery responses must not overwrite captured device models.
- Battery hydration is event/manual-refresh driven, selected-first, maximum concurrency 2, with no timer or aggressive polling.
- Preserve Mica, the existing TitleBar/tray shell, Fluent controls, product terminology, and evidence boundaries for A2DP/HFP.
- Use semantic ThemeResources and meet keyboard, screen-reader, high-contrast, and 200% text-scale requirements.
- Add no package dependency.
- Keep `README.md` and `docs/implementation-correction-spec.md` unchanged.
- Implement with red-green-refactor. Do not advance past a task until its focused tests and smoke check pass.

---

## File responsibility map

### New files

| File | Responsibility |
|---|---|
| `src/BTHaven.Core/Devices/BluetoothDeviceSelectionPolicy.cs` | Pure current/preferred/first visible resolution |
| `src/BTHaven.Core/Battery/BatteryTelemetryCoordinator.cs` | Bounded selected-first battery query/cache/generation handling |
| `src/BTHaven.App/MainPage.Selection.cs` | LocalSettings persistence, row reconciliation, selection epoch |
| `src/BTHaven.App/MainPage.Telemetry.cs` | Coordinator lifecycle, DispatcherQueue updates, list/detail joins |
| `tests/BTHaven.Core.Tests/BluetoothDeviceSelectionPolicyTests.cs` | Policy A transition table |
| `tests/BTHaven.Core.Tests/BatteryTelemetryCoordinatorTests.cs` | Query ordering/concurrency/staleness/last-known/prune contracts |

### Modified files

| File | Responsibility in this change |
|---|---|
| `src/BTHaven.Core/Devices/BluetoothDeviceModel.cs` | Endpoint observation state and preferred connection selector |
| `src/BTHaven.Core/Devices/BluetoothDeviceProjection.cs` | Populate endpoint state from one observation |
| `src/BTHaven.Windows/Bluetooth/BluetoothDeviceManager.cs` | Connected-first scalar provenance while preserving aggregate semantics |
| `src/BTHaven.App/DeviceRowViewModel.cs` | Row battery/RSSI/time/accessibility text |
| `src/BTHaven.App/MainPage.xaml` | Fluent IA, commands, SelectorBar views, semantic resources, adaptive states |
| `src/BTHaven.App/MainPage.xaml.cs` | Use selection/telemetry coordinators and epoch guards; remove stale model battery write |
| `src/BTHaven.App/MainPage.Actions.cs` | Ensure A2DP-triggered row refresh does not clear logical selection |
| `tests/BTHaven.Core.Tests/BluetoothDeviceModelTests.cs` | Preferred endpoint ordering |
| `tests/BTHaven.Core.Tests/BluetoothDeviceProjectionTests.cs` | Endpoint state projection |
| `tests/BTHaven.IntegrationTests/BluetoothDeviceManagerTests.cs` | Connected scalar provenance and deterministic multiple-connected merge |

## Dependency order

```text
Task 1 endpoint contract/provenance
   ├── Task 3 telemetry coordinator
   └── Task 4 selection integration
Task 2 selection policy
   └── Task 4 selection integration
Task 3 telemetry coordinator
   └── Task 5 telemetry presentation
Task 4 selection integration
   ├── Task 5 telemetry presentation
   └── Task 6 information architecture
Task 5 telemetry presentation
   └── Task 6 information architecture
Task 6 information architecture
   └── Task 7 adaptivity/accessibility/final verification
```

## Verification command conventions

Run commands from repository root in Windows PowerShell:

```powershell
$dotnet = "$env:USERPROFILE\.dotnet\dotnet.exe"
```

Focused Core tests:

```powershell
& $dotnet test tests/BTHaven.Core.Tests/BTHaven.Core.Tests.csproj -c Release -p:Platform=x64 --filter "FullyQualifiedName~BluetoothDeviceSelectionPolicyTests"
```

Focused integration tests:

```powershell
& $dotnet test tests/BTHaven.IntegrationTests/BTHaven.IntegrationTests.csproj -c Release -p:Platform=x64 --filter "FullyQualifiedName~BluetoothDeviceManagerTests"
```

Build and package-aware launch, only at checkpoints after the running BTHaven instance has been closed through its own UI:

```powershell
& $dotnet build BTHaven.slnx -c Release -p:Platform=x64
& $dotnet msbuild src/BTHaven.App/BTHaven.App.csproj -t:Run -p:Platform=x64 -p:Configuration=Release -p:WinAppRunDetach=true
```

Do not run `BTHaven.App.exe` directly.

---

### Task 1: Preserve endpoint state and prefer the connected observation

**Files:**
- Modify: `src/BTHaven.Core/Devices/BluetoothDeviceModel.cs:36-68`
- Modify: `src/BTHaven.Core/Devices/BluetoothDeviceProjection.cs:61-95`
- Modify: `src/BTHaven.Windows/Bluetooth/BluetoothDeviceManager.cs:592-646`
- Test: `tests/BTHaven.Core.Tests/BluetoothDeviceModelTests.cs`
- Test: `tests/BTHaven.Core.Tests/BluetoothDeviceProjectionTests.cs`
- Test: `tests/BTHaven.IntegrationTests/BluetoothDeviceManagerTests.cs`

**Interfaces:**
- Produces: `BluetoothEndpointReference.IsConnected`, `.IsPresent`, `.ObservedAt`.
- Produces: `BluetoothEndpointSelection.SelectPreferredConnection(BluetoothDeviceModel)`.
- Preserves: logical `Id`, aggregate `Transport`, `IsConnected`, `IsPresent`, capabilities, services, profiles.

- [ ] **Step 1: Add failing endpoint preference tests**

Add cases that make the intended ordering observable:

```csharp
[Fact]
public void Preferred_connection_chooses_connected_endpoint_before_transport_order()
{
    var device = Device(
        Endpoint("classic", BluetoothTransport.Classic, connected: false, present: true, observedAt: T(2)),
        Endpoint("ble", BluetoothTransport.LowEnergy, connected: true, present: true, observedAt: T(1)));

    var selected = BluetoothEndpointSelection.SelectPreferredConnection(device);

    Assert.Equal("ble", selected!.Id);
}

[Fact]
public void Preferred_connection_breaks_multiple_connected_ties_deterministically()
{
    var newer = Endpoint("z", BluetoothTransport.LowEnergy, true, true, T(3));
    var older = Endpoint("a", BluetoothTransport.Classic, true, true, T(2));

    Assert.Equal("z", BluetoothEndpointSelection.SelectPreferredConnection(Device(older, newer))!.Id);
}
```

Add projection assertions that one observation copies `IsConnected`, `IsPresent`, and `ObservedAt` into its endpoint reference.

Add manager tests with a disconnected Classic observation and connected BLE observation sharing one logical identity. Assert the aggregate remains DualMode/connected and scalar `Name`, `Address`, and `Category` prefer BLE. Add a two-connected case to prove timestamp/transport/ID tie ordering.

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
& $dotnet test tests/BTHaven.Core.Tests/BTHaven.Core.Tests.csproj -c Release -p:Platform=x64 --filter "FullyQualifiedName~BluetoothDeviceModelTests|FullyQualifiedName~BluetoothDeviceProjectionTests"
& $dotnet test tests/BTHaven.IntegrationTests/BTHaven.IntegrationTests.csproj -c Release -p:Platform=x64 --filter "FullyQualifiedName~BluetoothDeviceManagerTests"
```

Expected: compile/test failures because endpoint state and preferred selector do not exist and manager scalar provenance still follows transport order.

- [ ] **Step 3: Add endpoint state and the canonical selector**

Implement the contract exactly:

```csharp
public bool? IsConnected { get; init; }
public bool? IsPresent { get; init; }
public DateTimeOffset ObservedAt { get; init; }

public static BluetoothEndpointReference? SelectPreferredConnection(BluetoothDeviceModel device)
{
    ArgumentNullException.ThrowIfNull(device);
    return device.Endpoints
        .OrderByDescending(endpoint => endpoint.IsConnected == true)
        .ThenByDescending(endpoint => endpoint.IsPresent == true)
        .ThenByDescending(endpoint => endpoint.ObservedAt)
        .ThenBy(endpoint => endpoint.Transport)
        .ThenBy(endpoint => endpoint.Id, StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault();
}
```

Populate the fields in `BluetoothDeviceProjection.ToModel` and every endpoint reference created by `BluetoothDeviceManager.Merge`.

- [ ] **Step 4: Make manager scalar provenance use the same priority**

In `Merge`, order observations once using connected, present, observed time, transport, and ID. Use the first nonempty value from that ordered sequence for name/manufacturer/model/address/category, while leaving aggregate flags and aggregate transport calculations unchanged.

Do not introduce a second endpoint selector or name-based identity merge.

- [ ] **Step 5: Run focused tests and verify GREEN**

Run the two commands from Step 2. Expected: all selected tests pass, including existing logical identity and partial-removal tests.

- [ ] **Step 6: Commit the endpoint increment**

```powershell
git add src/BTHaven.Core/Devices/BluetoothDeviceModel.cs src/BTHaven.Core/Devices/BluetoothDeviceProjection.cs src/BTHaven.Windows/Bluetooth/BluetoothDeviceManager.cs tests/BTHaven.Core.Tests/BluetoothDeviceModelTests.cs tests/BTHaven.Core.Tests/BluetoothDeviceProjectionTests.cs tests/BTHaven.IntegrationTests/BluetoothDeviceManagerTests.cs
git commit -m "fix: prefer connected Bluetooth endpoint metadata"
```

---

### Task 2: Encode approved selection policy A

**Files:**
- Create: `src/BTHaven.Core/Devices/BluetoothDeviceSelectionPolicy.cs`
- Create: `tests/BTHaven.Core.Tests/BluetoothDeviceSelectionPolicyTests.cs`

**Interfaces:**
- Produces: `BluetoothDeviceSelectionPolicy.Resolve(IReadOnlyList<BluetoothDeviceModel>, string?, string?) -> string?`.
- Consumes later: exact visible list order from MainPage.

- [ ] **Step 1: Write the failing state-table tests**

Use one theory with explicit expected IDs:

```csharp
[Theory]
[MemberData(nameof(Cases))]
public void Resolve_follows_policy_A(
    string[] visibleIds,
    string? currentId,
    string? preferredId,
    string? expectedId)
{
    var visible = visibleIds.Select(Device).ToArray();

    var actual = BluetoothDeviceSelectionPolicy.Resolve(visible, currentId, preferredId);

    Assert.Equal(expectedId, actual, ignoreCase: true);
}

public static TheoryData<string[], string?, string?, string?> Cases => new()
{
    { ["connected-other", "chosen-disconnected"], "chosen-disconnected", "chosen-disconnected", "chosen-disconnected" },
    { ["first", "preferred"], null, "preferred", "preferred" },
    { ["first", "second"], "hidden", "missing", "first" },
    { [], "old", "old", null },
    { ["CaseSensitive"], "casesensitive", null, "CaseSensitive" },
};
```

Add a separate assertion that a connected item earlier in the list does not replace a visible current disconnected selection.

- [ ] **Step 2: Run the focused test and verify RED**

```powershell
& $dotnet test tests/BTHaven.Core.Tests/BTHaven.Core.Tests.csproj -c Release -p:Platform=x64 --filter "FullyQualifiedName~BluetoothDeviceSelectionPolicyTests"
```

Expected: FAIL because the policy type is absent.

- [ ] **Step 3: Implement the minimal resolver**

```csharp
public static string? Resolve(
    IReadOnlyList<BluetoothDeviceModel> visibleDevices,
    string? currentDeviceId,
    string? preferredDeviceId)
{
    ArgumentNullException.ThrowIfNull(visibleDevices);

    static bool Same(string left, string? right) =>
        right is not null && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    return visibleDevices.FirstOrDefault(device => Same(device.Id, currentDeviceId))?.Id
        ?? visibleDevices.FirstOrDefault(device => Same(device.Id, preferredDeviceId))?.Id
        ?? visibleDevices.FirstOrDefault()?.Id;
}
```

No connection-state branch is allowed here; first-item connected preference comes only from list ordering.

- [ ] **Step 4: Run the focused test and verify GREEN**

Run Step 2. Expected: all selection policy cases pass.

- [ ] **Step 5: Commit the selection policy**

```powershell
git add src/BTHaven.Core/Devices/BluetoothDeviceSelectionPolicy.cs tests/BTHaven.Core.Tests/BluetoothDeviceSelectionPolicyTests.cs
git commit -m "feat: define persistent device selection policy"
```

---

### Task 3: Build bounded battery telemetry state

**Files:**
- Create: `src/BTHaven.Core/Battery/BatteryTelemetryCoordinator.cs`
- Create: `tests/BTHaven.Core.Tests/BatteryTelemetryCoordinatorTests.cs`

**Interfaces:**
- Consumes: `IBatteryService.GetBatteryAsync(BluetoothDeviceModel, CancellationToken)`.
- Produces: `BatteryTelemetryStatus`, `BatteryTelemetryEntry`, `BatteryTelemetryChangedEventArgs`, and `BatteryTelemetryCoordinator` exactly as specified.
- Guarantee: maximum two provider calls; selected ID starts first; stale generation cannot publish over newer state.

- [ ] **Step 1: Write failing ordering and concurrency tests**

Use a controllable fake `IBatteryService` that records starts and exposes one `TaskCompletionSource<BatteryState>` per device.

```csharp
[Fact]
public async Task Refresh_starts_priority_device_first_and_limits_concurrency_to_two()
{
    var service = new ControlledBatteryService();
    var coordinator = new BatteryTelemetryCoordinator(service, maxConcurrency: 2);
    var refresh = coordinator.RefreshAsync(
        [Device("alpha"), Device("selected"), Device("omega")],
        priorityDeviceId: "selected");

    await service.WaitForStartsAsync(2);

    Assert.Equal("selected", service.Starts[0]);
    Assert.Equal(2, service.PeakConcurrency);
    service.CompleteAll(Available(70));
    await refresh;
}
```

- [ ] **Step 2: Write failing stale/last-known/prune tests**

Cover these contracts:

```csharp
[Fact]
public async Task Older_generation_cannot_replace_newer_battery_result() { /* controlled completion order */ }

[Fact]
public async Task Unavailable_attempt_retains_last_available_reading() { /* 80%, then unavailable */ }

[Fact]
public void Prune_removes_unknown_ids_but_not_filtered_ids() { /* valid ID set controls removal */ }
```

The unavailable assertion requires `Status == Unavailable`, `Current == null`, and `LastAvailable.Percentage == 80`.

- [ ] **Step 3: Run the focused test and verify RED**

```powershell
& $dotnet test tests/BTHaven.Core.Tests/BTHaven.Core.Tests.csproj -c Release -p:Platform=x64 --filter "FullyQualifiedName~BatteryTelemetryCoordinatorTests"
```

Expected: compile failure because telemetry types are absent.

- [ ] **Step 4: Implement the coordinator minimally**

Implement an injected constructor so tests control concurrency:

```csharp
public BatteryTelemetryCoordinator(IBatteryService service, int maxConcurrency = 2)
{
    ArgumentNullException.ThrowIfNull(service);
    ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);
    this.service = service;
    concurrency = new SemaphoreSlim(maxConcurrency, maxConcurrency);
}
```

Store entries and per-device generation under one lock. Order work with priority ID first, then connected, name, and ID. Skip `IsPresent == false`. Publish Loading before the provider call, then Available/Unavailable/Failed only when the captured generation remains current. Preserve `LastAvailable` on nonavailable outcomes.

Do not use `Task.Run`, a timer, or unbounded `Task.WhenAll` over provider calls. The semaphore is the resource boundary.

- [ ] **Step 5: Run focused tests and verify GREEN**

Run Step 3. Expected: all coordinator tests pass deterministically without sleeps.

- [ ] **Step 6: Run the whole Core suite**

```powershell
& $dotnet test tests/BTHaven.Core.Tests/BTHaven.Core.Tests.csproj -c Release -p:Platform=x64
```

Expected: PASS; no existing battery provider behavior changes.

- [ ] **Step 7: Commit the telemetry core**

```powershell
git add src/BTHaven.Core/Battery/BatteryTelemetryCoordinator.cs tests/BTHaven.Core.Tests/BatteryTelemetryCoordinatorTests.cs
git commit -m "feat: coordinate bounded battery telemetry"
```

## Checkpoint 1: Core contracts

- [ ] Run both Core and Integration test projects; all non-hardware tests pass.
- [ ] Review the public endpoint/selection/telemetry contracts against the spec.
- [ ] Confirm no WinUI/WinRT type entered `BTHaven.Core`.
- [ ] Confirm no second identity or endpoint-correlation algorithm was added.

---

### Task 4: Reconcile and persist selection atomically

**Files:**
- Create: `src/BTHaven.App/MainPage.Selection.cs`
- Modify: `src/BTHaven.App/MainPage.xaml.cs:29-47,49-79,142-217,260-333,466-564,781-792,1029-1083`
- Modify: `src/BTHaven.App/MainPage.Actions.cs:70-158`

**Interfaces:**
- Consumes: `BluetoothDeviceSelectionPolicy.Resolve`.
- Produces internally: `ReconcileRowsAndSelection`, `SetSelectedDevice`, `IsCurrentSelection`, persistence key `PreferredBluetoothDeviceId`.
- Preserves: `DeviceList.ItemsSource = Rows`, existing action handlers and services.

- [ ] **Step 1: Add a debug-only characterization check for the reconciliation boundary**

Because WinUI page tests are not currently hosted, keep the testable decision in Task 2 and add assertions in the new partial around impossible states:

```csharp
private void AssertSelectionInvariant()
{
    System.Diagnostics.Debug.Assert(
        selectedDeviceId is null
            ? DeviceList.SelectedItem is null
            : DeviceList.SelectedItem is DeviceRowViewModel row
                && string.Equals(row.Id, selectedDeviceId, StringComparison.OrdinalIgnoreCase));
}
```

The invariant is called after reconciliation, never from transient collection-change callbacks.

- [ ] **Step 2: Add persistence and epoch state**

In `MainPage.Selection.cs`, define:

```csharp
private const string PreferredDeviceIdSettingKey = "PreferredBluetoothDeviceId";
private bool isReconcilingSelection;
private long selectionEpoch;
private string? preferredDeviceId;

private static string? LoadPreferredDeviceId() =>
    ApplicationData.Current.LocalSettings.Values[PreferredDeviceIdSettingKey] as string;

private static void SavePreferredDeviceId(string deviceId) =>
    ApplicationData.Current.LocalSettings.Values[PreferredDeviceIdSettingKey] = deviceId;
```

Load once after `InitializeComponent`. Persist only on a real user selection, not automatic fallback, filter reconciliation, or watcher update.

- [ ] **Step 3: Replace clear-and-restore with one reconciliation transaction**

Before mutating `Rows`, compute visible models and the resolved ID. Set `isReconcilingSelection = true`, synchronize rows, set `DeviceList.SelectedItem`, then clear the flag. Call `SetSelectedDevice` only if the logical ID changed.

Required skeleton:

```csharp
private void ReconcileRowsAndSelection(IReadOnlyList<BluetoothDeviceModel> visible)
{
    var nextId = BluetoothDeviceSelectionPolicy.Resolve(visible, selectedDeviceId, preferredDeviceId);
    isReconcilingSelection = true;
    try
    {
        SynchronizeRows(visible);
        DeviceList.SelectedItem = Rows.FirstOrDefault(row => SameId(row.Id, nextId));
    }
    finally
    {
        isReconcilingSelection = false;
    }

    SetSelectedDevice(nextId);
    AssertSelectionInvariant();
}
```

`SynchronizeRows` may replace row objects but must not interpret collection events as selection intent. If incremental diffing becomes longer than the existing rebuild, keep the rebuild under the guard; correctness precedes micro-optimization.

- [ ] **Step 4: Make SelectionChanged represent user intent only**

Return immediately when `isReconcilingSelection`. For a valid row, update both selected/preferred, persist, increment epoch, render, and start capabilities. Do not clear persistence when the list is temporarily empty.

```csharp
if (isReconcilingSelection)
{
    return;
}
if (DeviceList.SelectedItem is DeviceRowViewModel row && devices.ContainsKey(row.Id))
{
    preferredDeviceId = row.Id;
    SavePreferredDeviceId(row.Id);
    SetSelectedDevice(row.Id);
}
```

- [ ] **Step 5: Guard async capabilities by ID and epoch**

Capture `var epoch = selectionEpoch` before starting the query and use:

```csharp
private bool IsCurrentSelection(string deviceId, long epoch) =>
    epoch == selectionEpoch
    && string.Equals(selectedDeviceId, deviceId, StringComparison.OrdinalIgnoreCase)
    && devices.ContainsKey(deviceId);
```

Remove `devices[device.Id] = device with { Battery = battery }`. Battery moves to Task 5's coordinator. All A2DP/HFP/volume UI writes require `IsCurrentSelection`.

Invalidate `selectedInspection` when its endpoint set/connection provenance no longer matches the selected model; do not render an old snapshot as current after a connection transition.

- [ ] **Step 6: Route every row refresh through reconciliation**

Update manual refresh, ApplyDeviceChange, filter changes, A2DP StateChanged, media toggle finalization, and battery/telemetry callbacks to call the guarded reconciliation method. Remove direct `Rows.Clear`/SelectedItem restoration and direct ClearSelection calls for transient updates.

- [ ] **Step 7: Build and run the app for a focused manual RED/GREEN scenario**

Build/launch with the commands under Verification conventions. Exercise:

1. select a disconnected device while a connected device is above it;
2. click Atualizar;
3. toggle filters away and back;
4. connect/disconnect another device;
5. wait for capability/battery work;
6. restart the app.

Expected: the selected logical row/details remain aligned; restart restores it if visible; invalid selection resolves to the first visible row; no blank detail exists with a nonempty list.

- [ ] **Step 8: Commit selection integration**

```powershell
git add src/BTHaven.App/MainPage.Selection.cs src/BTHaven.App/MainPage.xaml.cs src/BTHaven.App/MainPage.Actions.cs
git commit -m "fix: preserve device selection across live updates"
```

---

### Task 5: Join battery telemetry into rows and selected details

**Files:**
- Create: `src/BTHaven.App/MainPage.Telemetry.cs`
- Modify: `src/BTHaven.App/DeviceRowViewModel.cs`
- Modify: `src/BTHaven.App/MainPage.xaml.cs`
- Modify: `src/BTHaven.App/MainPage.xaml`

**Interfaces:**
- Consumes: `BatteryTelemetryCoordinator`, `BatteryTelemetryEntry`, preferred endpoint selector.
- Produces row properties: `BatteryText`, `TelemetryText`, `TelemetryAutomationText`, `ConnectionTransportText`.
- Keeps telemetry cache separate from `devices`.

- [ ] **Step 1: Define the row presentation contract**

Change the constructor to accept telemetry explicitly:

```csharp
public DeviceRowViewModel(
    BluetoothDeviceModel model,
    BatteryTelemetryEntry? telemetry = null,
    bool mediaEnabled = false)
```

Expose these immutable strings:

```csharp
public string BatteryText { get; }
public string TelemetryText { get; }
public string TelemetryAutomationText { get; }
public string MediaAutomationName { get; }
```

Formatting rules are exact:

- current percentage: `"68%"`; charging appends `" · carregando"` to accessible telemetry;
- last available only: row prefix `"Última bateria: 68%"` and include local `LastUpdated:HH:mm`;
- no current/last sample: visual `"—"`, accessible `"Bateria indisponível"`;
- RSSI: `"−42 dBm"` using a Unicode minus or culture-safe signed integer;
- observed: `"observado HH:mm:ss"`;
- media automation: `"Áudio de mídia para {model.Name}"`.

- [ ] **Step 2: Instantiate and own telemetry in the page partial**

After constructing `batteryService`, construct `BatteryTelemetryCoordinator`. Subscribe once in the page constructor and unsubscribe/cancel through the existing page lifetime.

Handle `Changed` through DispatcherQueue:

```csharp
private void BatteryTelemetry_Changed(object? sender, BatteryTelemetryChangedEventArgs e)
{
    DispatcherQueue.TryEnqueue(() =>
    {
        if (disposed || !devices.ContainsKey(e.Entry.DeviceId)) return;
        RefreshRows();
        if (SameId(selectedDeviceId, e.Entry.DeviceId)) RenderSelectedTelemetry(e.Entry);
    });
}
```

`RefreshRows` resolves telemetry with `TryGet` and does not mutate `BluetoothDeviceModel.Battery`.

- [ ] **Step 3: Trigger bounded hydration at explicit lifecycle points**

After initial/manual enumeration, call `Prune(devices.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase))`, then fire `RefreshAsync` for visible present models with `selectedDeviceId` as priority. Trigger a single-device refresh on add, disconnected→connected, endpoint-set change, and selection lacking a current sample.

Do not await all row telemetry before completing device enumeration/status. Observe/log coordinator exceptions without blocking list rendering.

- [ ] **Step 4: Add row and selected-detail telemetry UI**

Bind a dedicated telemetry TextBlock in each row. Replace the generic media automation name with `MediaAutomationName`. In Resumo, add values for preferred connection transport, RSSI, last device observation, battery source, confidence, and battery observation time.

When only `LastAvailable` exists, show `Última leitura` and its timestamp. Never show last-known data as current.

- [ ] **Step 5: Build and smoke-test telemetry states**

Launch the packaged app and inspect at least:

- current battery with/without charging;
- no battery property/GATT support;
- RSSI absent;
- selected device first during refresh;
- disconnect after a valid battery sample;
- filter that hides but does not prune a cached device;
- return to that filter and verify the last sample remains labeled.

Expected: list appears before all battery calls complete; no selection jump occurs as entries transition Loading→terminal; connection status cannot regress after a battery result.

- [ ] **Step 6: Commit telemetry presentation**

```powershell
git add src/BTHaven.App/MainPage.Telemetry.cs src/BTHaven.App/DeviceRowViewModel.cs src/BTHaven.App/MainPage.xaml.cs src/BTHaven.App/MainPage.xaml
git commit -m "feat: show per-device battery telemetry"
```

## Checkpoint 2: Behavioral integration

- [ ] Run Core and Integration test projects.
- [ ] Build the full solution x64 Release.
- [ ] Run the selection scenario from Task 4 and telemetry scenario from Task 5 together.
- [ ] Confirm every visible nonempty list has exactly one selected/rendered device.
- [ ] Confirm provider logs show no more than two concurrent hydration calls and no timer-driven repetition.

---

### Task 6: Reorganize commands and selected-device information

**Files:**
- Modify: `src/BTHaven.App/MainPage.xaml`
- Modify: `src/BTHaven.App/MainPage.xaml.cs`

**Interfaces:**
- Preserves existing named handlers for Logs, Diagnostics, Refresh, Inspect, media audio, auto-reconnect, endpoint, remote volume, and HFP.
- Produces stable views: `Resumo`, `Áudio`, `Diagnóstico` through native `SelectorBar`.

- [ ] **Step 1: Replace page-local hard-coded brushes with semantic Fluent resources**

Keep the existing panel radius and Mica shell. Set panel/card backgrounds and strokes from ThemeResources such as `CardBackgroundFillColorDefaultBrush` and `CardStrokeColorDefaultBrush`. Do not add a custom palette.

- [ ] **Step 2: Rescope the command surfaces**

Implement this hierarchy:

```xml
<Grid RowDefinitions="Auto,Auto,*" RowSpacing="16">
    <Grid x:Name="ProductHeader" Grid.Row="0" ColumnDefinitions="*,Auto">
        <StackPanel Spacing="4">
            <TextBlock Text="Bluetooth Hub" Style="{StaticResource TitleTextBlockStyle}" />
            <TextBlock Text="Observação real do Windows para Bluetooth Classic/LE, perfis, bateria e áudio"
                       TextWrapping="Wrap" />
        </StackPanel>
        <CommandBar x:Name="GlobalCommandBar" Grid.Column="1">
            <AppBarButton Label="Logs" Click="LogsButton_Click" />
            <AppBarButton Label="Diagnósticos" Click="DiagnosticsButton_Click" />
        </CommandBar>
    </Grid>
    <InfoBar x:Name="StatusInfoBar" Grid.Row="1" IsOpen="True" />
    <Grid x:Name="MasterDetailRegion" Grid.Row="2" ColumnDefinitions="320,*" ColumnSpacing="18">
        <Border x:Name="DevicePane" Grid.Column="0" />
        <Border x:Name="SelectedDevicePane" Grid.Column="1" />
    </Grid>
</Grid>
```

Move FilterComboBox and an F5-enabled Refresh AppBarButton into the device-pane header. Remove the arbitrary right margin and fixed filter width. Keep all commands below MainWindow's drag region.

- [ ] **Step 3: Replace the long detail stack with stable Operate views**

Keep selected device name, connection badge, and observation time outside view switching. Add a `SelectorBar` with `Resumo`, `Áudio`, and `Diagnóstico` items and one content grid per view.

Move existing controls without renaming handlers:

- Resumo: Connection, Battery, Capabilities.
- Áudio: A2DP, output endpoint, remote volume, HFP.
- Diagnóstico: Inspect action/status/text.

Do not duplicate controls or capability queries across views. HFP remains honest and disabled/not applicable for non-smartphones.

- [ ] **Step 4: Add hierarchy and state accessibility**

Set heading levels, filter header/name, AppBarButton labels, and bound per-device toggle names. Ensure status updates are announced without repeatedly interrupting users for noncritical telemetry.

Use text plus color for connection/battery states. Ensure values wrap and labels keep a readable minimum rather than relying on ellipsis for the only copy of a value.

- [ ] **Step 5: Build and inspect the Wide surface**

At a window width above 1100 effective px, verify:

- title and global CommandBar align without collision;
- filter/refresh read as list-scoped actions;
- status remains visible while the selected view scrolls;
- all prior actions remain reachable and invoke their original handlers;
- switching views does not change device selection or rerun capability queries;
- visual identity remains Mica + translucent Fluent cards.

- [ ] **Step 6: Commit information architecture**

```powershell
git add src/BTHaven.App/MainPage.xaml src/BTHaven.App/MainPage.xaml.cs
git commit -m "refactor: clarify Bluetooth device information hierarchy"
```

---

### Task 7: Add adaptive WinUI topology and complete accessibility verification

**Files:**
- Modify: `src/BTHaven.App/MainPage.xaml`
- Modify: `src/BTHaven.App/MainPage.xaml.cs`

**Interfaces:**
- Uses: `VisualStateManager`, `AdaptiveTrigger`, `SplitView` and existing selection state.
- Produces: Wide (`>=1100`), Medium (`720-1099`), Compact (`<720`) layouts.

- [ ] **Step 1: Add Wide and Medium visual states**

Wide uses one-row title/CommandBar and `320,*` master/detail. Medium moves CommandBar under the title and uses `280,*`. Use setter-targeted grid rows/columns and widths; do not handle `SizeChanged` in code-behind.

- [ ] **Step 2: Add Compact one-pane behavior**

Compact shows the list pane first. Selecting a row reveals selected detail in a `SplitView` content pane with a labeled Back command. Back returns to list without clearing selection or preferred persistence.

Code-behind only coordinates pane state and focus:

```csharp
private void ShowCompactDetails(DeviceRowViewModel row)
{
    DeviceSplitView.IsPaneOpen = false;
    SelectedDeviceName.Focus(FocusState.Programmatic);
}

private void CompactBackButton_Click(object sender, RoutedEventArgs e)
{
    DeviceSplitView.IsPaneOpen = true;
    DeviceList.Focus(FocusState.Programmatic);
}
```

If runtime behavior shows `SplitView.IsPaneOpen` semantics differ for the chosen display mode, adjust the control properties while preserving list→detail→Back focus order.

- [ ] **Step 3: Verify keyboard and screen-reader names**

Keyboard path:

1. Tab to filter and Refresh; F5 refreshes.
2. Arrow through ListView; selection and details stay aligned.
3. Tab into the media toggle; accessible name includes the device.
4. Navigate SelectorBar and content without losing device selection.
5. In Compact, Enter opens detail; Escape/Back returns to the originating row.

Run Accessibility Insights/NVDA and inspect the app AX tree. Expected: page/pane headings are ordered; global status is announced appropriately; toggles are distinguishable; disabled HFP/media actions expose reason text nearby.

- [ ] **Step 4: Verify scale, theme, and responsive thresholds**

Inspect Wide, Medium, and Compact at 100%, 150%, and 200% text scaling in light, dark, and high-contrast modes. Confirm no overlap/clipping, all commands remain reachable, list telemetry wraps/shortens predictably, and the detail value grids stack or wrap before becoming unreadable.

Thresholds may be tuned from 1100/720 only when screenshots show a concrete collision. Record the final values in XAML; do not add runtime magic numbers elsewhere.

- [ ] **Step 5: Run complete verification**

```powershell
& $dotnet test tests/BTHaven.Core.Tests/BTHaven.Core.Tests.csproj -c Release -p:Platform=x64
& $dotnet test tests/BTHaven.IntegrationTests/BTHaven.IntegrationTests.csproj -c Release -p:Platform=x64
& $dotnet build BTHaven.slnx -c Release -p:Platform=x64
```

Launch packaged app and repeat the combined acceptance matrix:

- 0, 1, and multiple visible devices;
- multiple connected devices;
- selected device disconnects;
- filter hides current then reveals preferred;
- partial and final endpoint removal;
- watcher refresh while battery/A2DP/HFP queries are in flight;
- app restart with valid and obsolete persisted IDs;
- current, unavailable, failed, and last-known battery;
- Wide/Medium/Compact keyboard path.

Expected: every acceptance criterion in the spec passes without false HFP/A2DP claims.

- [ ] **Step 6: Run source-focused review before final commit**

Confirm:

- no direct `Rows.Clear` path can call `ClearSelection` as user intent;
- no battery result writes a captured model back to `devices`;
- only one connected-endpoint selector exists;
- no polling timer or new dependency exists;
- `README.md`, `docs/implementation-correction-spec.md`, MainWindow shell, tray behavior, and capability declarations are unchanged.

- [ ] **Step 7: Commit adaptive/accessibility completion**

```powershell
git add src/BTHaven.App/MainPage.xaml src/BTHaven.App/MainPage.xaml.cs
git commit -m "feat: adapt Bluetooth workspace for compact windows"
```

## Final checkpoint

- [ ] Core and Integration suites pass.
- [ ] x64 Release solution build succeeds.
- [ ] Package-aware smoke run verifies selection, endpoint, telemetry, actions, and all responsive states.
- [ ] AX inspection confirms unique device action names and logical focus order.
- [ ] Light, dark, high contrast, 100%, 150%, and 200% text-scale evidence is captured for review.
- [ ] Every spec acceptance criterion maps to a passing test or named manual scenario above.
- [ ] No scope-protected file changed.

## Risks during execution

| Risk | Early signal | Containment |
|---|---|---|
| WinUI collection events still clear selection | `SelectionChanged` logs “cleared” during refresh | Guard every programmatic sync; assert invariant after transaction |
| App test seam is too coupled to WinUI | Selection logic cannot be exercised without packaged runtime | Keep resolver in Core and page code as a thin transaction |
| Battery provider hangs or serializes device discovery | list/status waits for telemetry | Never await coordinator before list render; max concurrency 2; lifetime cancellation |
| Late same-ID task overwrites current UI | battery/A2DP result appears after reselection | ID + `selectionEpoch` guard; coordinator per-device generation |
| Endpoint scalar provenance changes correlation | A2DP/HFP match count regresses | Preserve logical identity and endpoint list; run manager/correlation tests before UI integration |
| SelectorBar/compact pane loses hidden control state | action status resets on view switch | Reuse one control instance per view; change visibility, not recreate content |
| Text scaling breaks label/value grids | 200% screenshot overlaps | Medium/Compact states stack grids; wrap values; no fixed 150 px label columns |
| Persisted ID never returns | obsolete LocalSettings value | Treat as soft preference; first visible fallback; never block list |

## Review gate

This plan is intentionally complete but not self-authorizing. Stop after saving it and wait for human review. Implementation begins only after the reviewer approves this document and chooses an execution workflow.
