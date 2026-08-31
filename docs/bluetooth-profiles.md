# Bluetooth profiles and public Windows surfaces

This document records the evidence boundary for BTHaven. A successful build or device enumeration is not treated as proof of profile negotiation or audible/call audio.

## Profile map

| Scenario | Role wanted by BTHaven | Windows surface | Current classification |
|---|---|---|---|
| Inventory | observe paired/present/connected devices | `DeviceWatcher`, `Windows.Devices.Bluetooth`, `Windows.Devices.Enumeration` | `SUPPORTED_PUBLIC` |
| Battery properties | read exposed association properties | `DeviceInformation.Properties`, `System.Devices.*` | `SUPPORTED_PUBLIC` when exposed; otherwise unavailable |
| BLE battery | GATT client | `BluetoothLEDevice`, Battery Service `0x180F`, Battery Level `0x2A19` | `SUPPORTED_PUBLIC` when the device exposes it |
| Phone media to PC | Windows acts as A2DP Sink | `Windows.Media.Audio.AudioPlaybackConnection` | `SUPPORTED_PUBLIC` for the documented remote-playback path |
| Phone calls | Windows acts as a generic HFP Hands-Free Unit | `PhoneLineTransportDevice` and Bluetooth/profile APIs | `UNKNOWN` / restricted on the current system |
| PC endpoint listing | enumerate render/capture endpoints | Core Audio / WASAPI | `SUPPORTED_PUBLIC` |

## Live evidence from the development machine

The probes ran on Windows `10.0.26200`, x64, with a Bluetooth adapter reporting Classic and Low Energy support. The paired Android phone appeared as one Classic and one BLE association endpoint sharing a container ID. The Classic endpoint reported `IsConnected=true` during the latest run.

### A2DP

`AudioPlaybackConnection.GetDeviceSelector()` returned one target for the phone. Exercising the first ID returned directly by that selector produced:

```text
A2DP.Connection.Started
A2DP.Connection.StateChanged: Opened
A2DP.Connection.OpenResult: Success
A2DP.Connection.Disposed
```

The public Microsoft guidance describes this exact sequence:

1. call `GetDeviceSelector()`;
2. discover a `DeviceInformation` target;
3. call `TryCreateFromId` with that target's exact ID;
4. call `StartAsync()`;
5. call `OpenAsync()`;
6. keep the connection object alive while audio is expected;
7. observe state changes and dispose it when finished.

The current machine's default render endpoint is `Speakers (PRO X 2 LIGHTSPEED)`. `AudioPlaybackConnection` routes through the Windows system playback endpoint; its public API does not expose an arbitrary per-connection WASAPI endpoint selector. BTHaven therefore warns when the selected endpoint is not the Windows default instead of pretending to route it.

The remaining physical acceptance step is audible playback: start media on the phone, select the PC as the phone's Bluetooth media output, activate BTHaven A2DP, and listen on the Windows default headset.

### HFP

The runtime type and `CallsPhoneContract` v5 are present. The selector returned one concrete Bluetooth phone-line transport with:

```text
AudioRoutingStatus=CanRouteToLocalDevice
InBandRingingEnabled=true
IsRegistered=false
```

The live action run returned:

```text
RequestAccessAsync -> DeniedBySystem
RegisterApp        -> UnauthorizedAccessException, HRESULT 0x80070005
ConnectAsync       -> not reached after registration denial
```

Microsoft documents `phoneLineTransportManagement` as a restricted capability for the access and registration operations. This does not prove a generic third-party HFP Hands-Free Unit implementation. BTHaven exposes a test button that calls the real API and displays the result; it never reports HFP audio as active from enumeration alone.

## Device state semantics

The application keeps these observations separate:

- **paired:** Windows has a pairing association;
- **present:** the association endpoint is visible to Windows;
- **connected:** the relevant endpoint reports an active connection;
- **available:** the requested profile API returns a target that can be created/used.

A single logical phone may have separate Classic and BLE endpoints. BTHaven groups them by `System.Devices.Aep.ContainerId` when available, while retaining the source transport and raw observation information in local diagnostics.

## Battery behavior

Battery results come from a provider chain:

1. Windows association properties;
2. standard BLE GATT Battery Service and Battery Level characteristic;
3. vendor providers only as future explicit extensions.

No percentage is synthesized. If neither a percentage nor a trustworthy charging state is available, the model reports `unavailable` and logs the provider results.

## Official references

- [Enable audio playback from remote Bluetooth-connected devices](https://learn.microsoft.com/en-us/windows/apps/develop/media-playback/enable-remote-audio-playback)
- [AudioPlaybackConnection class](https://learn.microsoft.com/en-us/uwp/api/windows.media.audio.audioplaybackconnection?view=winrt-28000)
- [Enumerate devices](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/enumerate-devices)
- [Device information properties](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/device-information-properties)
- [DeviceWatcher class](https://learn.microsoft.com/en-us/uwp/api/windows.devices.enumeration.devicewatcher?view=winrt-28000)
- [BluetoothDevice class](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothdevice?view=winrt-28000)
- [Bluetooth Classic Audio](https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/bluetooth-classic-audio)
- [PhoneLineTransportDevice](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.calls.phonelinetransportdevice?view=winrt-28000)
- [PhoneLineTransportDevice.RequestAccessAsync](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.calls.phonelinetransportdevice.requestaccessasync?view=winrt-28000)
- [PhoneLineTransportDevice.RegisterApp](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.calls.phonelinetransportdevice.registerapp?view=winrt-28000)
- [Bluetooth GATT Client](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-client)
