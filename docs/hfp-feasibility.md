# HFP feasibility gate

**Current gate result:** `BLOCKED_FOR_END_TO_END_TEST` on the development machine. The Windows API type and contract are present, but no `PhoneLineTransportDevice` target was exposed and no Android/iPhone call could be exercised. The generic HFP Hands-Free Unit role therefore remains `UNKNOWN`; this run does **not** justify a global `NOT_AVAILABLE` claim.

## Question

Can a normal third-party Windows 11 desktop application make a paired Android/iPhone phone keep its normal HFP Audio Gateway role while the Windows PC acts as the HFP Hands-Free Unit, with bidirectional call audio routed to PC endpoints?

```text
phone (HFP AG) <── HFP/SCO/eSCO ──> Windows PC (wanted HFP HF)
                                      ├── downlink -> render endpoint
                                      └── uplink <- capture endpoint
```

## Environment and run evidence

| Field | Observed |
|---|---|
| Windows | `Microsoft Windows 10.0.26200` (`10.0.26200.9168` from `ver`) |
| Windows SDK | `10.0.26100.0` |
| .NET | SDK `10.0.400`, runtime `.NET 10.0.11`, `win-x64` |
| HFP probe | `2026-08-30T18:44:53Z` (discovery-only) and `2026-08-30T18:51:48Z` (all opt-in flags) |
| API type | `PhoneLineTransportDevice`: present |
| API contract | `CallsPhoneContract` v5: present |
| selector | created successfully; Windows returned a Bluetooth phone-line interface selector |
| transport devices | `0` |
| access/register/connect | not run because there was no concrete transport device |

## Exact probe evidence

The `04-phone-hfp` executable emitted:

```text
HFP.ApiPresence: typePresent=true, contractV5Present=true
HFP.Selector.Created: success
HFP.TransportDevicesFound: count=0
HFP.TransportUnavailable: no PhoneLineTransportDevice for the current user/system state
HFP.TransportOperation.NotRun: request-access/register/connect require a concrete device
HFP.ProbeBoundary: genericHfpHandsFreeRoleProven=false
```

The probe was also run with `--request-access --register --connect`; because the selector returned zero targets, those operations were explicitly recorded as `NotRun`. There is no access-denied HRESULT to report for `RequestAccessAsync`. Reporting an HRESULT here would be fabricated. The next required test is on a machine with an actually paired phone that exposes the Windows phone-line transport path.

## Official API evidence

| Operation | API / contract | Capability named by Microsoft | Evidence status | Classification |
|---|---|---|---|---|
| Detect API type | `Windows.ApplicationModel.Calls.PhoneLineTransportDevice` | none stated on type page | present in the current runtime | `SUPPORTED_PUBLIC` for type availability |
| Enumerate transport devices | `PhoneLineTransportDevice.GetDeviceSelector()` | none stated on method summary | selector created, zero current targets | `UNKNOWN` for a usable phone path |
| Request access | `PhoneLineTransportDevice.RequestAccessAsync()` | `phoneLineTransportManagement` | API reference requires the restricted capability; no target was available to invoke it | `SUPPORTED_RESTRICTED` |
| Register app | `PhoneLineTransportDevice.RegisterApp()` | `phoneLineTransportManagement` | registration semantics documented; no target was available to invoke it | `SUPPORTED_RESTRICTED` |
| Connect transport | `PhoneLineTransportDevice.ConnectAsync()` | not stated on the method summary | no target was available to invoke it | `UNKNOWN` |
| Generic HFP HF role for arbitrary phone | no public generic HFP-HF registration surface identified by the current evidence | n/a | not proven by `PhoneLineTransportDevice` documentation or this run | `UNKNOWN` |
| Bidirectional call PCM on selected PC endpoints | no public generic HFP-HF desktop API identified | microphone/audio access is a separate concern | acceptance test not run | `UNKNOWN` |

## Core Audio companion probe

`05-call-audio-routing` executed successfully and enumerated:

- 3 active render endpoints;
- 2 active capture endpoints;
- 2 unplugged render endpoints and 2 unplugged capture endpoints;
- 1 disabled capture endpoint;
- default render and communications format: `48000Hz/2ch/Extensible`;
- default communications capture format: `48000Hz/2ch/IeeeFloat`;
- `0x88890004` was observed when querying the mix format of unplugged endpoints.

This proves that Core Audio endpoint discovery is available. It does not prove that an HFP call transport can be bound to those endpoints.

## Decision matrix if the public path is insufficient

| Alternative | Complexity | Latency | Quality | Android | iPhone | Root | Driver | Signing | Maintenance | UX | Status |
|---|---:|---:|---:|---|---|---|---|---|---|---|---|
| A. Public WinRT / Windows call transport | low–medium | potentially low | OS-managed | unknown | unknown | no | no | normal app | low | native | blocked pending phone target |
| B. MSIX plus restricted capability | medium | potentially low | OS-managed | unknown | unknown | no | no | package/signing | medium | native | blocked pending packaged probe |
| C. Windows-integrated component | high | low | OS-managed | unknown | unknown | no | possibly | vendor/platform | high | native | not selected |
| D. Custom Bluetooth/profile driver | very high | low–medium | controllable | likely | likely | no | yes | signed driver | very high | installation burden | not selected |
| E. Dedicated Bluetooth dongle controlled by the app | high | low–medium | controllable | likely | likely | no | possibly | hardware/driver | high | extra hardware | not selected |
| F. Phone companion + Wi-Fi/LAN | medium | medium | controllable | yes | yes | no | no | normal app | medium | requires phone app | not primary design |

## Gate rule

Do not build `IPhoneTransport` on a fake success path. If a phone-targeted run cannot obtain a public, supported transport and the real-phone acceptance test cannot pass, classify the requested HFP call feature as `BLOCKED` or `UNKNOWN`, document the exact reason, and stop the HFP-dependent implementation.

## Official references

- [PhoneLineTransportDevice](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.calls.phonelinetransportdevice?view=winrt-28000)
- [PhoneLineTransportDevice.RequestAccessAsync](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.calls.phonelinetransportdevice.requestaccessasync?view=winrt-28000)
- [PhoneLineTransportDevice.RegisterApp](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.calls.phonelinetransportdevice.registerapp?view=winrt-28000)
- [App capability declarations](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations)
- [Bluetooth Classic Audio](https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/bluetooth-classic-audio)
- [Call Windows Runtime APIs in desktop apps](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-apis-desktop-apps)
