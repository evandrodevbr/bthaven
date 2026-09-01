# Phase 0 probe results

This file is the sanitized evidence ledger for the current Windows machine. It is updated from command output, not from compilation alone. Raw logs stay out of git because they can contain device names, IDs, Bluetooth addresses, endpoint names, and local paths.

## Environment

| Field | Value |
|---|---|
| Windows build | `Microsoft Windows 10.0.26200` |
| Windows SDK | `10.0.26100.0` include/lib |
| .NET SDK | `10.0.400` |
| .NET runtime | `10.0.11`, `win-x64` |
| MSBuild | `18.9.6+14fbf8d52` |
| Bluetooth adapter | available; Classic and Low Energy both supported |
| Current refresh | `2026-09-01T14:00:27Z`-`14:01:45Z` |
| Verified HEAD | `606606e9615e44b8e714be7e1d016fb4d989d33b` |

## Status summary

```text
DEVICE ENUMERATION: PASS       (adapter, paired snapshots, and watchers completed)
BATTERY:            PARTIAL    (no Windows controller; GATT source unreachable)
A2DP SINK:          NOT RUN    (discovery only; no device ID or open attempt)
HFP PHONE LINK:     NOT RUN    (discovery only; activation flags were omitted)
CALL AUDIO INVENTORY: PASS     (endpoint counts and default roles observed)
INSPECTION:         PASS       (read-only summary; unavailable fields retained)
REMOTE VOLUME:      NOT EXPOSED (public Windows application surface)
```

## Per-probe evidence

### 01 - device enumeration: PASS

- adapter returned `available=true`;
- Classic support: `true`;
- Low Energy support: `true`;
- Classic paired snapshot: count `1`;
- Classic connected snapshot: count `0`;
- BLE paired snapshot: count `1`;
- BLE connected snapshot: count `0`;
- both paired watchers reached `EnumerationCompleted` and `Stopped`;
- `Probe.Completed` reported `uniqueDeviceCount=2` with a one-second watch.

This run proves read-only enumeration and watcher lifecycle. It did not observe a connected endpoint.

### 02 - battery: PARTIAL

- Windows battery controllers: `0`;
- Bluetooth association devices inspected: `1`;
- GATT devices: `1`;
- GATT Battery Service query status: `Unreachable`;
- GATT service count: `0`;
- battery result: unavailable; no percentage was invented.

The process completed successfully, but the current environment did not expose a readable battery source.

### 03 - A2DP sink: NOT RUN

- Windows audio playback selector returned `1` target;
- the probe was run without `--device-id`;
- no `StartAsync` or `OpenAsync` call was attempted;
- process exit code: `0`;
- event: `A2DP.Sink.NotExercised`.

This is discovery evidence only. It intentionally does not claim an A2DP connection or audible playback.

### 04 - HFP phone link: DISCOVERY ONLY

- `PhoneLineTransportDevice` API type: present;
- `CallsPhoneContract` v5: present;
- selector creation: succeeded;
- transport devices found: `1`;
- observed transport: Bluetooth;
- observed `AudioRoutingStatus`: `CanRouteToLocalDevice`;
- initial registration: `false`;
- generic HFP Hands-Free Unit role: not proven.

The probe was run with no arguments. `RequestAccessAsync`, `RegisterApp`, and `ConnectAsync` were not run; therefore HFP activation was not exercised. No `--request-access`, `--register`, or `--connect` flag was passed.

### 05 - Core Audio endpoint inventory: PASS

- active render endpoints: `3`;
- disabled render endpoints: `0`;
- unplugged render endpoints: `2`;
- active capture endpoints: `2`;
- disabled capture endpoints: `1`;
- unplugged capture endpoints: `2`;
- default render Multimedia role: observed;
- default render Communications role: observed;
- default capture Communications role: observed.

This proves public endpoint discovery and default-role selection. It does not prove HFP call PCM or bidirectional call audio.

### 06 - full Bluetooth inspection: PASS with explicit unavailable fields

- one paired device was selected for read-only inspection;
- snapshot endpoint count: `4`;
- Classic endpoint count: `2`;
- BLE endpoint count: `2`;
- GATT service count: `0`;
- RFCOMM service count: `0`;
- battery observation: `Unavailable`, confidence `Unknown`;
- A2DP profile: `Available`;
- HFP profile: `Discovered`;
- remote-volume status: `NotExposed`;
- diagnostic count: `10`;
- process exit code: `0`.

The inspector retained unavailable states instead of inventing battery, GATT, RFCOMM, or remote-volume capabilities.

### Remote volume boundary

The implementation and probes intentionally do not change phone volume. The public `AudioPlaybackConnection` surface exposes the A2DP playback connection lifecycle, not a general application-level AVRCP volume command. `WindowsRemoteVolumeService` reports this boundary explicitly.

## Build and test evidence

### Build

```text
powershell.exe -NoProfile -Command 'git worktree add --detach "$env:TEMP\bthaven-verify-606606e" 606606e'
powershell.exe -NoProfile -Command 'Set-Location "$env:TEMP\bthaven-verify-606606e"; & "$env:USERPROFILE\.dotnet\dotnet.exe" restore BTHaven.slnx -p:Platform=x64'
Restored; 12 projects.

powershell.exe -NoProfile -Command 'Set-Location "$env:TEMP\bthaven-verify-606606e"; & "$env:USERPROFILE\.dotnet\dotnet.exe" build BTHaven.slnx -c Release -p:Platform=x64 --no-restore'
Succeeded; 12/12 projects, 0 warnings, 0 errors.

powershell.exe -NoProfile -Command 'Set-Location "$env:TEMP\bthaven-verify-606606e"; & "$env:USERPROFILE\.dotnet\dotnet.exe" build src/BTHaven.App/BTHaven.App.csproj -c Release -p:Platform=x64 --no-restore'
Succeeded; 0 warnings, 0 errors.

powershell.exe -NoProfile -Command 'Set-Location "$env:TEMP\bthaven-verify-606606e"; & "$env:USERPROFILE\.dotnet\dotnet.exe" build src/BTHaven.Windows/BTHaven.Windows.csproj -c Release -p:Platform=x64 --no-restore'
Succeeded; 0 warnings, 0 errors.
```

The clean detached worktree at HEAD `606606e` restored the solution and built all 12 project entries with zero warnings and zero errors. Separate App and Windows Release x64 builds also completed with zero warnings and zero errors.

### Source graph

The clean detached source graph scan reported `111` files, `13,097` lines, `0` parsed import edges, and quality signal `8768`. DSM statistics reported `edge_count=0`, `density=0`, and clean layering. The C# project graph was independently verified through `BTHaven.slnx`, which contains `12` project entries; the zero parsed-edge result is scanner output, not evidence that the projects have no dependencies.

The tracked correlation, inspection, remote-volume, and App source graph is included in this checkout.

### Tests

The required serial no-build runs and the standard runs were both executed with `-m:1`.

| Mode | Project or filter | Result |
|---|---|---|
| `--no-build` | `BTHaven.Core.Tests` | `22` total, `22` passed, `0` failed; exit `0` |
| `--no-build` | `BTHaven.IntegrationTests` | `62` total, `60` passed, `2` failed; exit `1` |
| standard | `BTHaven.Core.Tests` | `22` total, `22` passed, `0` failed; exit `0` |
| standard | `BTHaven.IntegrationTests` | `62` total, `60` passed, `2` failed; exit `1` |
| standard and `--no-build`, targeted | A2DP sink and auto-reconnect | `14` total, `14` passed, `0` failed; exit `0` |
| standard, targeted | conflicting ContainerId/address precedence | `1` total, `1` passed, `0` failed; exit `0` |
| standard, manager focus | `BluetoothDeviceManagerTests` | `10` total, `10` passed, `0` failed; exit `0` |
| `--no-build`, deterministic focus | manager, correlation, GATT, audio lifecycle, HFP, diagnostics, remote-volume | `54` total, `54` passed, `0` failed; exit `0` |

The two Integration failures are explicit environment limits, not skipped tests:

1. `BluetoothDeviceInspectorTests.Inspection_includes_classic_and_ble_endpoints_for_the_connected_phone` failed with `Connected dual-mode Bluetooth hardware phone required.` The current run had no connected dual-mode phone.
2. `WindowsAudioServicesSmokeTests.A2dp_service_opens_the_first_windows_remote_audio_target` failed because `A2dpSinkService.ConnectAsync` returned `false` (`Assert.True` expected `true`). Discovery found a target, but this Windows device/runtime did not open it.

The final audit of commits `5315e91`, `4fa3740`, and `606606e` against the requirements found no unresolved P0/P1: identity promotion and conflicting ContainerId precedence remain covered, and A2DP disable clears the logical-to-selector binding while invalidating exact-endpoint fallback.

## Dependency and secrets scans

```text
powershell.exe -NoProfile -Command '& "$env:USERPROFILE\.dotnet\dotnet.exe" list BTHaven.slnx package --vulnerable --include-transitive'
No vulnerable packages reported for all 12 projects using the current NuGet source.

where.exe osv-scanner
Not available on this machine.

detect-secrets scan
Results: {}.

detect-secrets scan src tests probes --exclude-files ".*(\\\\|/)bin(\\\\|/).*|.*(\\\\|/)obj(\\\\|/).*"
Results: {}.
```

The second secrets scan covered source, test, and probe trees while excluding generated `bin` and `obj` content. No secret values were recorded.

## Commands run in this refresh

### SDK, builds, and tests

```powershell
git worktree add --detach "$env:TEMP\bthaven-verify-606606e" 606606e
Set-Location "$env:TEMP\bthaven-verify-606606e"
& "$env:USERPROFILE\.dotnet\dotnet.exe" --info
& "$env:USERPROFILE\.dotnet\dotnet.exe" restore BTHaven.slnx -p:Platform=x64
& "$env:USERPROFILE\.dotnet\dotnet.exe" build BTHaven.slnx -c Release -p:Platform=x64 --no-restore
& "$env:USERPROFILE\.dotnet\dotnet.exe" build src/BTHaven.App/BTHaven.App.csproj -c Release -p:Platform=x64 --no-restore
& "$env:USERPROFILE\.dotnet\dotnet.exe" build src/BTHaven.Windows/BTHaven.Windows.csproj -c Release -p:Platform=x64 --no-restore
& "$env:USERPROFILE\.dotnet\dotnet.exe" test tests/BTHaven.Core.Tests/BTHaven.Core.Tests.csproj -c Release -m:1 --no-build --logger "console;verbosity=normal"
& "$env:USERPROFILE\.dotnet\dotnet.exe" test tests/BTHaven.IntegrationTests/BTHaven.IntegrationTests.csproj -c Release -m:1 --no-build --logger "console;verbosity=normal"
& "$env:USERPROFILE\.dotnet\dotnet.exe" test tests/BTHaven.Core.Tests/BTHaven.Core.Tests.csproj -c Release -m:1 --logger "console;verbosity=normal"
& "$env:USERPROFILE\.dotnet\dotnet.exe" test tests/BTHaven.IntegrationTests/BTHaven.IntegrationTests.csproj -c Release -m:1 --logger "console;verbosity=normal"
& "$env:USERPROFILE\.dotnet\dotnet.exe" test tests/BTHaven.IntegrationTests/BTHaven.IntegrationTests.csproj -c Release -m:1 --no-build --filter "FullyQualifiedName~A2dpAutoReconnectServiceTests|FullyQualifiedName~A2dpSinkServiceTests" --logger "console;verbosity=minimal"
& "$env:USERPROFILE\.dotnet\dotnet.exe" test tests/BTHaven.IntegrationTests/BTHaven.IntegrationTests.csproj -c Release -m:1 --filter "FullyQualifiedName~A2dpAutoReconnectServiceTests|FullyQualifiedName~A2dpSinkServiceTests" --logger "console;verbosity=minimal"
& "$env:USERPROFILE\.dotnet\dotnet.exe" test tests/BTHaven.IntegrationTests/BTHaven.IntegrationTests.csproj -c Release -m:1 --filter "FullyQualifiedName~Conflicting_container_ids_with_same_address_remain_separate_models" --logger "console;verbosity=minimal"
& "$env:USERPROFILE\.dotnet\dotnet.exe" test tests/BTHaven.IntegrationTests/BTHaven.IntegrationTests.csproj -c Release -m:1 --filter "FullyQualifiedName~BluetoothDeviceManagerTests" --logger "console;verbosity=minimal"
& "$env:USERPROFILE\.dotnet\dotnet.exe" test tests/BTHaven.IntegrationTests/BTHaven.IntegrationTests.csproj -c Release -m:1 --no-build --filter "FullyQualifiedName~BluetoothDeviceManagerTests|FullyQualifiedName~BluetoothDeviceInspectorMatchingTests|FullyQualifiedName~GattBatteryProviderTests|FullyQualifiedName~DiagnosticsExporterTests|FullyQualifiedName~TraceDiagnosticLoggerTests|FullyQualifiedName~HfpPhoneTransportServiceTests|FullyQualifiedName~HfpPhoneTransportServiceActivationTests|FullyQualifiedName~A2dpAutoReconnectServiceTests|FullyQualifiedName~A2dpSinkServiceTests|FullyQualifiedName~RemoteVolumeServiceTests" --logger "console;verbosity=minimal"
git diff --check HEAD^ HEAD
```

### Read-only probes

```powershell
powershell.exe -NoProfile -Command '& "./probes/01-device-enumeration/bin/Release/net10.0-windows10.0.26100.0/BTHaven.Probe.DeviceEnumeration.exe" --watch-seconds 1'
powershell.exe -NoProfile -Command '& "./probes/02-battery/bin/Release/net10.0-windows10.0.26100.0/BTHaven.Probe.Battery.exe"'
powershell.exe -NoProfile -Command '& "./probes/03-a2dp-sink/bin/Release/net10.0-windows10.0.26100.0/BTHaven.Probe.A2dpSink.exe"'
powershell.exe -NoProfile -Command '& "./probes/04-phone-hfp/bin/Release/net10.0-windows10.0.26100.0/BTHaven.Probe.PhoneHfp.exe"'
powershell.exe -NoProfile -Command '& "./probes/05-call-audio-routing/bin/Release/net10.0-windows10.0.26100.0/BTHaven.Probe.CallAudioRouting.exe"'
powershell.exe -NoProfile -Command '& "./probes/06-device-inspection/bin/Release/net10.0-windows10.0.26100.0/BTHaven.Probe.DeviceInspection.exe"'
```

All six probe processes were rerun read-only and exited `0` between `14:00:32Z` and `14:01:39Z`. Probe 03 omitted `--device-id`; probe 04 omitted `--request-access`, `--register`, and `--connect`; no WinUI UI runtime, MSIX identity, packaging, installation, or activation command was run.

## Explicit boundaries

- HFP access, registration, and connection activation were not run; no HFP activation flag was passed.
- A2DP open/hold and audible playback were not run by the probes.
- No WinUI UI runtime was launched, and no MSIX publish, packaging, install, registration, deployment, or activation command was run.
- The two hardware-dependent Integration failures above remain visible; they were not converted into skips or masked.
