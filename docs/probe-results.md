# Phase 0 probe results

This file is the evidence ledger for the current Windows machine. It is updated from command output, not from compilation alone. Raw logs are intentionally kept out of git because they can contain device names, IDs, Bluetooth addresses, and endpoint names.

## Environment

| Field | Value |
|---|---|
| Windows build | `Microsoft Windows 10.0.26200` |
| Windows SDK | `10.0.26100.0` include/lib |
| .NET SDK | `10.0.400` installed per-user at `C:\Users\evand\.dotnet` |
| .NET runtime | `10.0.11`, `win-x64` |
| Bluetooth adapter | available; Classic and Low Energy both supported |
| Current probe run | `2026-08-31T01:46:26Z`–`01:47:36Z` |

## Status summary

```text
DEVICE ENUMERATION: PASS    (paired Android phone observed; Classic endpoint connected)
BATTERY:            PARTIAL (Windows controller absent; association/GATT sources unavailable)
A2DP SINK:          PARTIAL (official target opened with Success; audible playback not yet listened to)
HFP PHONE LINK:     BLOCKED (transport found, access DeniedBySystem, RegisterApp 0x80070005)
HFP CALL AUDIO:     BLOCKED (registration/connect and bidirectional call test not available)
```

## Per-probe evidence

### 01 — device enumeration: PASS

- adapter returned `available=true`;
- `IsClassicSupported=true`;
- `IsLowEnergySupported=true`;
- `classic-paired`: count `1`;
- `classic-connected`: count `1`;
- `ble-paired`: count `1`;
- `ble-connected`: the snapshot returned `0x80004004` during one transient query, while the BLE paired watcher completed normally;
- both paired watchers reached `EnumerationCompleted` and `Stopped`;
- the Classic and BLE observations shared one logical container and the same phone name.

The app must continue to distinguish the Classic connected endpoint from the BLE paired/present endpoint; they are not interchangeable states.

### 02 — battery: PARTIAL

- Windows battery controllers: `0`;
- Bluetooth association devices inspected: `1`;
- Windows battery properties were present as null for the phone;
- BLE Battery Service discovery returned `Unreachable` with service count `0`;
- no percentage was invented.

The current phone therefore reports `battery=unavailable`, which is the correct result for the observed sources.

### 03 — A2DP sink: PARTIAL, connection API passed

- `AudioPlaybackConnection.GetDeviceSelector()` returned `1` target for the paired phone;
- the probe used the first ID returned directly by that selector (`--exercise-first`), avoiding shell escaping;
- `StartAsync()` completed;
- `StateChanged` reported `Opened`;
- `OpenAsync()` returned `Success`;
- the connection stayed alive for the requested hold period and was disposed cleanly;
- the current default Windows render endpoint is the headset `Speakers (PRO X 2 LIGHTSPEED)`.

This is a positive API/transport result. The remaining acceptance step is audible playback: start media on the phone, select the PC as the phone's Bluetooth media output, activate BTHaven, and listen on the Windows default headset. The public API routes through the Windows system default endpoint; it does not expose an arbitrary per-connection WASAPI endpoint selector.

### 04 — HFP phone link: BLOCKED

- `PhoneLineTransportDevice` type: present;
- `CallsPhoneContract` v5: present;
- `GetDeviceSelector()`: returned a selector;
- concrete transport devices: `1`;
- transport: Bluetooth;
- `AudioRoutingStatus`: `CanRouteToLocalDevice`;
- in-band ringing: `true`;
- initial registration: `false`;
- `RequestAccessAsync`: `DeniedBySystem`;
- `RegisterApp`: `UnauthorizedAccessException`, `HRESULT 0x80070005`;
- `ConnectAsync`: not reached after registration/access rejection;
- generic HFP Hands-Free Unit role: not proven.

Microsoft's API references require the restricted `phoneLineTransportManagement` capability for the access/registration operations. The app exposes a button that calls the real path and reports this result; it does not label HFP active after discovery alone.

### 05 — Core Audio endpoint inventory: PASS

- active render endpoints: `3`;
- active capture endpoints: `2`;
- default render and communications endpoint: `Speakers (PRO X 2 LIGHTSPEED)`;
- default communications capture endpoint: `Microphone (PRO X 2 LIGHTSPEED)`;
- default render format: `48000Hz/2ch/Extensible`;
- default capture format: `48000Hz/2ch/IeeeFloat`;
- unplugged mix-format queries returned `0x88890004`, logged rather than hidden.

This proves endpoint discovery and identifies the expected headset path. It does not prove HFP call PCM.

## Build and test evidence

```text
dotnet build BTHaven.slnx -c Release -p:Platform=x64 --no-restore
0 warnings, 0 errors

dotnet test BTHaven.slnx -c Release -p:Platform=x64 --no-restore
Core: 11 passed, 0 failed
Integration: 7 passed, 0 failed
```

## Reproduction commands

### PowerShell

```powershell
Set-Location 'C:\Users\evand\Documents\GitHub\bthaven'
$env:Path = "$env:USERPROFILE\.dotnet;$env:Path"
& "$env:USERPROFILE\.dotnet\dotnet.exe" run --project '.\probes\03-a2dp-sink\03-a2dp-sink.csproj' -c Release -- --exercise-first --hold-seconds 15
& "$env:USERPROFILE\.dotnet\dotnet.exe" run --project '.\probes\04-phone-hfp\04-phone-hfp.csproj' -c Release -- --request-access --register --connect
```

### Git Bash

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet run --project probes/01-device-enumeration -c Release -- --watch-seconds 5
dotnet run --project probes/02-battery -c Release
dotnet run --project probes/03-a2dp-sink -c Release -- --exercise-first --hold-seconds 15
dotnet run --project probes/04-phone-hfp -c Release -- --request-access --register --connect
dotnet run --project probes/05-call-audio-routing -c Release
```

## What remains before the gate can close

1. Run the updated BTHaven UI with the phone selected and click **Ativar áudio do smartphone**.
2. Start phone media and verify audible output on the Windows default headset.
3. Verify reconnect after the phone or A2DP target disappears and returns.
4. Keep HFP blocked until restricted capability approval and a real bidirectional call test exist.
5. Do not add a custom driver, HCI hook, or Phone Link reverse engineering silently.
