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
