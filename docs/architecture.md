# Architecture

BTHaven is split into a UI-free domain core, Windows adapters, and small executable probes. The boundary is intentional: the core must not depend on WinRT objects or WinUI types, and the UI must not become the place where Bluetooth policy is hidden.

```text
┌───────────────────────────────────────────────────────────┐
│ BTHaven.App                                               │
│ WinUI 3, tray, settings, diagnostics                      │
└───────────────────────┬───────────────────────────────────┘
                        │ interfaces / models
┌───────────────────────▼───────────────────────────────────┐
│ BTHaven.Core                                              │
│ Devices · Battery · Bluetooth · Audio · Calls · Diagnostics│
└───────────────┬─────────────────────────┬─────────────────┘
                │                         │
┌───────────────▼──────────────┐  ┌──────▼─────────────────┐
│ BTHaven.Windows              │  │ BTHaven.Native         │
│ WinRT · DeviceWatcher        │  │ C++/WinRT/Win32/COM     │
│ AudioPlaybackConnection      │  │ only when evidence      │
│ Core Audio / WASAPI          │  │ requires it             │
└──────────────────────────────┘  └────────────────────────┘
```

## Core contracts

The initial domain surface defines the seams needed by the requested product without importing Windows types:

- `IBluetoothDeviceService`
- `IBatteryService`
- `IMediaAudioSink`
- `IPhoneTransport`
- `ICallSession`
- `IAudioEndpointService`
- `IAudioRouter`
- `IAudioProcessingPipeline`

## Implemented Windows adapters

- `BluetoothDeviceManager` maintains paired Classic and BLE `DeviceWatcher` instances, groups endpoint observations by `ContainerId`, and keeps `paired`, `present`, and `connected` independent. Its default query is the connected filter; callers can request every supported filter.
- `WindowsBatteryService` composes `WindowsDevicePropertiesBatteryProvider` and `GattBatteryProvider`. The GATT provider supports one-shot reads and notification subscriptions for `0x180F` / `0x2A19` when the device exposes a readable/notify characteristic.
- `A2dpSinkService` wraps `AudioPlaybackConnection` and exposes explicit `Starting`, `Started`, `Opening`, `Opened`, and `Failed` states. It does not claim success when `TryCreateFromId` returns `null` or `OpenAsync` reports failure.
- `AudioEndpointManager` enumerates active WASAPI render/capture endpoints and stable endpoint IDs. It is an inventory adapter; it does not change the Windows default endpoint or introduce a virtual driver.

## Device state model

`paired`, `present`, and `connected` are separate observations. A paired device can be absent. A present device can be disconnected. A connected device can expose a different set of profile endpoints than the base association endpoint.

`DeviceWatcher` events are the primary update mechanism. Aggressive polling is not used when Windows can publish an event.

## Audio boundaries

A2DP sink and HFP call audio are distinct paths:

```text
remote phone ── A2DP ──> Windows AudioPlaybackConnection ──> system render endpoint
remote phone ── HFP  ──> Windows call transport / audio endpoint (gate)
PC microphone ── WASAPI ──> HFP uplink (gate)
```

The A2DP path is documented by Microsoft as a supported remote-audio playback scenario. That does not prove that a desktop app can register a generic HFP Hands-Free Unit. HFP remains a separate feasibility question and an explicit gate.

The current Windows implementation intentionally stops at the public boundary: endpoint selection for `AudioPlaybackConnection` and generic phone HFP registration still require separate evidence. No UI state is allowed to turn those unresolved paths into a false-positive capability.

## Battery provider order

```text
Windows association properties
          │ if unavailable
          ▼
GATT Battery Service (0x180F / 0x2A19)
          │ if unavailable
          ▼
vendor provider (future, opt-in, never guessed)
```

Every provider returns `null`/`unavailable` when Windows or the device does not expose a trustworthy value.

## Diagnostics

Structured events use an event name, UTC timestamp, and typed fields. Raw identifiers are collected only for local troubleshooting and are not intended for public reports. The future diagnostics export must redact or omit unnecessary device addresses, phone numbers, caller IDs, and audio data.
