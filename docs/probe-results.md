# Phase 0 probe results

This file is the evidence ledger for the current machine. It is updated from command output, not from compilation alone. Raw logs are intentionally kept out of git because they can contain device names, IDs, Bluetooth addresses, and endpoint names.

## Environment

| Field | Value |
|---|---|
| Windows build | `10.0.26200.9168` (`Microsoft Windows 10.0.26200` from the runtime) |
| Windows SDK | `10.0.26100.0` include/lib |
| .NET SDK | `10.0.400` installed per-user at `C:\Users\evand\.dotnet` |
| .NET runtime | `10.0.11`, `win-x64` |
| Bluetooth adapter | available; Classic and Low Energy both reported supported |
| Probe run window | `2026-08-30T18:44:49Z`–`18:44:55Z`; enumeration rerun at `18:46:37Z` after correcting an invalid property key; HFP opt-in run at `18:51:48Z` |

## Status summary

```text
DEVICE ENUMERATION: PASS (API smoke; no paired device was present for the physical matrix)
BATTERY:            PARTIAL
A2DP SINK:          BLOCKED (no A2DP target; audio not exercised)
HFP PHONE LINK:     BLOCKED (no PhoneLineTransportDevice target)
HFP CALL AUDIO:     BLOCKED (no phone/HFP transport; only endpoint inventory ran)
```

## Per-probe evidence

### 01 — device enumeration: PASS for API smoke

- adapter returned `available=true`;
- `IsClassicSupported=true`;
- `IsLowEnergySupported=true`;
- four snapshots completed with count `0` (`classic-paired`, `classic-connected`, `ble-paired`, `ble-connected`);
- both `DeviceWatcher` runs reached `EnumerationCompleted` and `Stopped`;
- no Bluetooth device was paired on this machine, so mouse/headset/phone acceptance was not run.

The first run exposed a real probe bug: requesting `System.Devices.AepContainerId` produced `0x8002802B` (`Property key syntax error`). The probe was corrected to the documented `System.Devices.Aep.ContainerId` key and rerun successfully.

### 02 — battery: PARTIAL

- Windows battery controllers: `0`;
- Bluetooth association devices inspected: `0`;
- paired BLE devices queried for GATT Battery Service: `0`;
- the probe correctly emitted `Battery.Windows.Unavailable` instead of inventing a percentage.

No positive Battery Service or Windows battery-property sample was available on this machine.

### 03 — A2DP sink: BLOCKED for end-to-end test

- `AudioPlaybackConnection.GetDeviceSelector()` executed;
- A2DP-capable device interfaces returned: `0`;
- `TryCreateFromId`, `StartAsync`, and `OpenAsync` were not run because there was no valid target ID;
- therefore no smartphone media stream was tested.

The API path is documented by Microsoft, but this run proves only selector enumeration, not audible A2DP playback.

### 04 — HFP phone link: BLOCKED for end-to-end test

- `PhoneLineTransportDevice` type: present;
- `CallsPhoneContract` v5: present;
- `GetDeviceSelector()`: returned a selector;
- transport devices: `0`;
- access, registration, and connection operations: not run because they require a concrete transport device;
- an explicit `--request-access --register --connect` run recorded `HFP.TransportOperation.NotRun` for the same reason;
- generic HFP Hands-Free Unit role: not proven.

This is a blocked test setup, not evidence that the public API is globally unavailable. See [`hfp-feasibility.md`](hfp-feasibility.md).

### 05 — Core Audio endpoint inventory: PASS

- endpoint enumeration completed for render/capture and active/disabled/unplugged states;
- active render endpoints: `3`;
- active capture endpoints: `2`;
- default communications render/capture endpoints were returned;
- default endpoint formats were observed at 48 kHz stereo on this machine;
- unplugged mix-format queries returned `0x88890004`, which was logged rather than hidden.

This probe does not open an HFP stream and therefore cannot mark call audio as passed.

## Build and test evidence

```text
dotnet build BTHaven.slnx -c Release --no-restore
0 warnings, 0 errors

dotnet test tests/BTHaven.Core.Tests/BTHaven.Core.Tests.csproj --no-restore
3 passed, 0 failed
```

## Reproduction commands

```text
dotnet run --project probes/01-device-enumeration -c Release -- --watch-seconds 5
dotnet run --project probes/02-battery -c Release
dotnet run --project probes/03-a2dp-sink -c Release
dotnet run --project probes/04-phone-hfp -c Release
dotnet run --project probes/05-call-audio-routing -c Release
```

## What remains before the gate can close

1. Pair a real Android 12+ phone and at least one Classic/BLE peripheral on a Windows 11 23H2+ test machine.
2. Rerun probes 01–04 and preserve redacted logs.
3. Run the HFP probe with explicit `--request-access --register --connect` only after a target is returned.
4. Execute the acceptance call with the phone answered on the phone, then verify downlink and uplink audio.
5. Add an MSIX/package-identity run before classifying restricted capability behavior as supported.
