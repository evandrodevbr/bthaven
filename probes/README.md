# Phase 0 probes

Each probe is a small executable with structured stdout logs. They intentionally fail closed: an absent device, unavailable capability, denied access, or unsupported profile is reported rather than converted into a fake success.

| Probe | Purpose |
|---|---|
| `01-device-enumeration` | snapshot and `DeviceWatcher` observations for Bluetooth Classic/BLE |
| `02-battery` | Windows battery reports, association properties, and GATT 0x180F/0x2A19 |
| `03-a2dp-sink` | `AudioPlaybackConnection` discovery and optional open/close exercise |
| `04-phone-hfp` | `PhoneLineTransportDevice` runtime, selector, access, registration, and connect evidence |
| `05-call-audio-routing` | Core Audio/WASAPI render/capture endpoint and format inventory |

The probes do not claim that a successful API call equals a successful end-to-end Bluetooth profile test. The phone, pairing state, adapter driver, Windows package identity, and the exact Windows build all matter.
