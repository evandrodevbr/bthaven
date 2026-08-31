# HFP feasibility gate

**Current gate result:** `BLOCKED_FOR_THIRD_PARTY_ACCESS` on the development machine. Windows exposes the phone-line API and a concrete transport for the paired Android phone, but access is denied before registration. The generic HFP Hands-Free Unit role and bidirectional call audio therefore remain unproven.

## Question

Can a normal third-party Windows 11 desktop application make a paired Android/iPhone phone keep its normal HFP Audio Gateway role while the Windows PC acts as the HFP Hands-Free Unit, with bidirectional call audio routed to PC endpoints?

```text
phone (HFP AG) <── HFP/SCO/eSCO ──> Windows PC (wanted HFP HF)
                                      ├── downlink -> render endpoint
                                      └── uplink <- capture endpoint
```

## Current live evidence

| Field | Observed |
|---|---|
| Windows | `Microsoft Windows 10.0.26200` |
| Windows SDK | `10.0.26100.0` |
| .NET | SDK `10.0.400`, runtime `.NET 10.0.11`, `win-x64` |
| Paired phone | one Android phone, observed through Classic and BLE association endpoints |
| HFP API type | `Windows.ApplicationModel.Calls.PhoneLineTransportDevice`: present |
| API contract | `CallsPhoneContract` v5: present |
| HFP selector | created successfully |
| HFP transport devices | `1` |
| HFP transport | `Bluetooth`, `AudioRoutingStatus=CanRouteToLocalDevice` |
| in-band ringing | `true` in the live probe |
| registered before action | `false` |
| `RequestAccessAsync` | `DeniedBySystem` |
| `RegisterApp` | `UnauthorizedAccessException`, `HRESULT 0x80070005` |
| `ConnectAsync` | not reached after registration was rejected |

The exact run was `04-phone-hfp --request-access --register --connect` on `2026-08-31`. The probe emitted the access status and the exception type/HRESULT. No call was answered and no call PCM was captured.

## Official API and capability evidence

| Operation | API / contract | Capability named by Microsoft | Current result | Classification |
|---|---|---|---|---|
| Detect API type | `PhoneLineTransportDevice` | none on type page | present | `SUPPORTED_PUBLIC` for type availability |
| Enumerate transport devices | `GetDeviceSelector()` + `DeviceInformation.FindAllAsync` | package/runtime context still matters | one concrete target | `SUPPORTED_RESTRICTED` for usable phone transport |
| Request access | `PhoneLineTransportDevice.RequestAccessAsync()` | `phoneLineTransportManagement` | `DeniedBySystem` | `SUPPORTED_RESTRICTED` |
| Register app | `PhoneLineTransportDevice.RegisterApp()` | `phoneLineTransportManagement` | `UnauthorizedAccessException`, `0x80070005` | `SUPPORTED_RESTRICTED` |
| Connect transport | `PhoneLineTransportDevice.ConnectAsync()` | restricted transport access | not reached after registration rejection | `UNKNOWN` |
| Generic HFP HF role for arbitrary phone | no generic public desktop registration surface proven | n/a | not proven by this target | `UNKNOWN` |
| Bidirectional call PCM on selected PC endpoints | no generic public HFP-HF PCM surface proven | microphone/audio access is separate | acceptance test not run | `UNKNOWN` |

Microsoft Learn references explicitly list `phoneLineTransportManagement` as the requirement for `RequestAccessAsync` and `RegisterApp`. It is a restricted capability; declaring it is not equivalent to receiving approval or making a sideloaded package trusted. BTHaven currently keeps this capability out of the normal manifest so the A2DP app remains installable and the HFP button reports the actual system denial.

## A2DP result on the same phone

The separate `03-a2dp-sink` probe found one target through `AudioPlaybackConnection.GetDeviceSelector()`. Exercising the first target directly from the selector produced:

```text
A2DP.Connection.Started
A2DP.Connection.StateChanged: Opened
A2DP.Connection.OpenResult: Success
A2DP.Connection.Disposed
```

This proves the public Windows A2DP connection path opens for the paired phone. It does not by itself prove audible output, because the phone must send media to the PC and the acceptance test must listen on the selected/default Windows endpoint. The current machine's default render endpoint is `Speakers (PRO X 2 LIGHTSPEED)`.

## Core Audio companion probe

`05-call-audio-routing` enumerated:

- 3 active render endpoints;
- 2 active capture endpoints;
- default render/communications endpoint: `Speakers (PRO X 2 LIGHTSPEED)`;
- default communications capture endpoint: `Microphone (PRO X 2 LIGHTSPEED)`;
- default render format: `48000Hz/2ch/Extensible`;
- default capture format: `48000Hz/2ch/IeeeFloat`;
- `0x88890004` for the mix format of unplugged endpoints.

This proves Core Audio endpoint discovery. It does not prove that Windows will bind a phone HFP call to those endpoints.

## Alternatives if HFP remains restricted

| Alternative | Complexity | Latency | Android | iPhone | Driver/signing | Status |
|---|---:|---:|---|---|---|---|
| Public WinRT phone transport | low–medium | low if authorized | unknown | unknown | restricted capability | blocked on current system |
| MSIX plus Microsoft-approved restricted capability | medium | low | unknown | unknown | package approval/signing | requires separate approval experiment |
| Windows-integrated component | high | low | likely | likely | possibly driver/vendor | not selected |
| Custom Bluetooth/profile driver | very high | low–medium | likely | likely | signed driver | not selected |
| Dedicated Bluetooth dongle with profile control | high | low–medium | likely | likely | hardware/driver | not selected |
| Phone companion plus Wi-Fi/LAN | medium | medium | yes | yes | no driver | fallback only |

## Gate rule

Do not report HFP as enabled just because a transport device is enumerated or `AudioRoutingStatus` says `CanRouteToLocalDevice`. The feature becomes `PASS` only after:

1. access is granted;
2. registration succeeds;
3. transport connection succeeds;
4. a call answered on the phone produces downlink audio on the chosen PC endpoint;
5. the selected PC microphone reaches the remote caller;
6. the call ends without leaving A2DP in a broken state.

## Official references

- [PhoneLineTransportDevice](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.calls.phonelinetransportdevice?view=winrt-28000)
- [PhoneLineTransportDevice.RequestAccessAsync](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.calls.phonelinetransportdevice.requestaccessasync?view=winrt-28000)
- [PhoneLineTransportDevice.RegisterApp](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.calls.phonelinetransportdevice.registerapp?view=winrt-28000)
- [Enable audio playback from remote Bluetooth-connected devices](https://learn.microsoft.com/en-us/windows/apps/develop/media-playback/enable-remote-audio-playback)
- [AudioPlaybackConnection](https://learn.microsoft.com/en-us/uwp/api/windows.media.audio.audioplaybackconnection?view=winrt-28000)
- [App capability declarations](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations)
- [Bluetooth Classic Audio](https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/bluetooth-classic-audio)
- [Call Windows Runtime APIs in desktop apps](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-apis-desktop-apps)
