# Known limitations and non-assumptions

These limitations are part of the initial design, not bugs to be hidden behind optimistic UI.

- A device can be paired but not present or connected.
- Windows device properties are optional. Battery must display `unavailable` when no trustworthy value exists.
- The standard GATT Battery Service is available only when the device exposes it and Windows permits the GATT operation.
- `AudioPlaybackConnection` documents the A2DP sink path to Windows audio endpoints. Its public surface does not promise arbitrary per-connection endpoint routing.
- The current phone's A2DP target opened successfully with `OpenAsync=Success`. This is transport evidence, not an audible assertion: the phone must select the PC as its Bluetooth media output, and Windows sends the stream to its default render endpoint.
- Windows Bluetooth Classic audio behavior for HFP headsets does not prove that a desktop app can register as a phone's generic HFP Hands-Free Unit.
- `PhoneLineTransportDevice` is associated with Windows `PhoneLine` integration. It must not be presented as a generic HFP profile implementation until an Android/iPhone acceptance test passes.
- On the current machine, HFP discovery returns a concrete transport, but `RequestAccessAsync` returns `DeniedBySystem` and `RegisterApp` returns `0x80070005`; the restricted `phoneLineTransportManagement` capability/approval gate is not closed.
- Call audio may use 8 kHz narrowband or 16 kHz wideband. The implementation must not force 48 kHz into an HFP stream.
- AEC, noise suppression, and AGC are future abstractions. The MVP prioritizes headsets; speaker-plus-microphone operation may lack AEC initially.
- No vendor-specific battery hacks, Phone Link reverse engineering, HCI injection, unsigned driver, or virtual audio driver is included without a written architecture decision.
- Probe results are machine- and device-dependent. A probe can prove an API path on the current machine, not universal compatibility.
