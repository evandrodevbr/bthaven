# Capabilities, packaging, and permissions

This table is deliberately conservative. A capability is not added to the future app merely because a feature sounds related to it. The exact probe outcome and the API reference must agree first.

| Capability / requirement | API or scenario | Current evidence | Classification |
|---|---|---|---|
| `bluetooth` | BLE/GATT APIs such as `GattServiceUuids.Battery` | Microsoft GATT API references list the Bluetooth app capability; the unpackaged Phase 0 console executed the API surface with zero paired BLE targets | `SUPPORTED_WITH_PACKAGING` for the manifest path; unpackaged behavior is device/runtime dependent |
| `microphone` | PC microphone capture for call audio | required by the future capture path; Phase 0 only enumerated capture endpoints and did not open a call stream | `UNKNOWN` |
| `phoneLineTransportManagement` | `PhoneLineTransportDevice.RequestAccessAsync`, registration/transport management | Microsoft lists it under **Restricted capabilities** and the API references explicitly require it for access/registration | `SUPPORTED_RESTRICTED` |
| `phoneCall` | phone-call related APIs | not inferred from Phone Link; no exact Phase 0 API use justifies requesting it | `UNKNOWN` |
| `phoneCallHistory` / `phoneCallHistorySystem` | call history APIs | outside the Phase 0 minimum; do not request | `UNKNOWN` |
| MSIX / package identity | restricted capability declarations and WinUI 3 deployment | the packaged BTHaven shell launches through the Windows App SDK `dotnet run` path; restricted HFP access remains separately denied | `SUPPORTED_WITH_PACKAGING` for deployment; HFP authorization remains `UNKNOWN` |
| Unpackaged WinUI runtime | Windows App SDK shell | official deployment guidance requires runtime/bootstrapper handling for unpackaged apps; HFP capability authorization was not proven by this console run | `SUPPORTED_WITH_PACKAGING` for the deployment mechanism; HFP authorization remains `UNKNOWN` |
| Administrator | normal inventory/A2DP/HFP public API use | no administrator assumption is made | `UNKNOWN` |
| driver signing | custom Bluetooth profile or virtual audio driver | not part of the public managed probe; only considered if evidence requires it | `REQUIRES_DRIVER` for that alternative, not for the current MVP path |

## What the Phase 0 run proved

- The Windows Runtime type `PhoneLineTransportDevice` was present on Windows build `10.0.26200`.
- `CallsPhoneContract` version 5 was reported present.
- `PhoneLineTransportDevice.GetDeviceSelector()` returned a selector.
- One concrete `PhoneLineTransportDevice` target was exposed for the paired Android phone; it reported `CanRouteToLocalDevice` and in-band ringing.
- `RequestAccessAsync` returned `DeniedBySystem`; `RegisterApp` raised `UnauthorizedAccessException` with `HRESULT 0x80070005`.
- `ConnectAsync` was not reached after access/registration denial. This is not proof that a generic HFP Hands-Free Unit is available to third-party desktop apps.

## Rules

1. Every future manifest capability must name the API and the evidence that requires it.
2. Restricted capabilities must be called out in release documentation, not hidden in a default manifest.
3. Unpackaged execution must be tested independently from MSIX execution.
4. A successful compile does not demonstrate capability authorization.
5. API presence does not demonstrate that the app can assume the HFP HF role.

## Official references

- [App capability declarations](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations)
- [Call Windows Runtime APIs in desktop apps](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-apis-desktop-apps)
- [PhoneLineTransportDevice.RequestAccessAsync](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.calls.phonelinetransportdevice.requestaccessasync?view=winrt-28000)
- [PhoneLineTransportDevice.RegisterApp](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.calls.phonelinetransportdevice.registerapp?view=winrt-28000)
- [Deploy unpackaged Windows App SDK apps](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-unpackaged-apps)
- [Bluetooth GATT Client](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-client)
