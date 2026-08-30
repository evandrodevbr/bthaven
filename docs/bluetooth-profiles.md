# Bluetooth profiles and public Windows surfaces

This document records the initial technical map for BTHaven. It is not a claim that every desired feature is available to third-party desktop software.

## Profile map

| Scenario | Bluetooth role wanted by BTHaven | Windows surface investigated | Initial classification |
|---|---|---|---|
| Inventory | observe paired/present/connected devices | `Windows.Devices.Enumeration`, `DeviceWatcher`, `Windows.Devices.Bluetooth` | `SUPPORTED_PUBLIC` for the documented enumeration surfaces; device properties remain device/stack dependent |
| Battery properties | read exposed Windows association properties | `DeviceInformation.Properties`, `System.Devices.*` | `SUPPORTED_PUBLIC` when Windows exposes the property; otherwise unavailable |
| BLE battery | GATT client | `BluetoothLEDevice`, `GattServiceUuids.Battery`, `GattCharacteristicUuids.BatteryLevel` | `SUPPORTED_PUBLIC` with the Bluetooth capability and a device that exposes the standard service |
| Phone media to PC | Windows is an A2DP Sink | `Windows.Media.Audio.AudioPlaybackConnection` | `SUPPORTED_PUBLIC` for the documented remote playback scenario, introduced in Windows 10 version 2004 |
| Phone calls | Windows is a generic HFP Hands-Free Unit | `Windows.ApplicationModel.Calls.PhoneLineTransportDevice` plus any Bluetooth/profile surface | `UNKNOWN` until the probe and packaging/capability evidence establish the exact role |
| PC endpoint listing | enumerate render/capture endpoints | Core Audio / WASAPI (`IMMDeviceEnumerator`) | `SUPPORTED_PUBLIC` for desktop applications |

## Probe evidence from this machine

The Phase 0 executables ran on Windows `10.0.26200.9168` with an adapter reporting Classic and Low Energy support. No Bluetooth devices were paired or exposed to the current user, so the snapshot and watcher counts were zero. The APIs themselves returned successfully after correcting the property key to the documented `System.Devices.Aep.ContainerId` spelling.

`AudioPlaybackConnection.GetDeviceSelector()` returned successfully but found zero A2DP sink targets. `PhoneLineTransportDevice` and `CallsPhoneContract` v5 were present and its selector was created, but zero phone-line transport devices were returned. These observations are machine-state evidence; they are not universal compatibility claims.

## Device state semantics

The application must not collapse these fields:

- **paired**: Windows has a pairing association;
- **present**: the association endpoint is currently visible to the system;
- **connected**: the requested device/profile reports an active connection;
- **available**: an API can create or use the requested profile connection.

A `DeviceInformation` object can represent a particular device kind or interface. The selector and `DeviceInformationKind` matter when correlating records.

## A2DP sink evidence

Microsoft's remote-audio playback guidance explicitly describes the PC behaving like a Bluetooth speaker for a phone. The documented sequence is:

1. create a watcher with `AudioPlaybackConnection.GetDeviceSelector()`;
2. call `AudioPlaybackConnection.TryCreateFromId(deviceId)`;
3. call `StartAsync()` to enable the connection;
4. call `OpenAsync()` to open it;
5. observe `StateChanged` and dispose the connection when finished.

The same guidance says audio is played through the system audio endpoints. The API reference does not expose a per-connection WASAPI endpoint selector. BTHaven therefore treats endpoint selection as a separate routing investigation instead of assuming the API supports arbitrary direct routing.

## HFP distinction

Windows documents HFP audio behavior for Bluetooth Classic audio devices and documents `PhoneLineTransportDevice` as a hardware device associated with a `PhoneLine`, currently supported for Bluetooth devices. These facts are not equivalent to a public, generic, third-party registration API for an arbitrary phone's HFP Audio Gateway connection.

The HFP probe records:

- whether the runtime type and `CallsPhoneContract` are present;
- whether `PhoneLineTransportDevice.GetDeviceSelector()` returns a selector;
- whether transport devices are exposed;
- access, registration, and connection results when a concrete device exists;
- exact exception/HRESULT values when an operation is rejected.

On the current machine the last three operations were not invoked because the selector returned zero targets.

## Official references

- [Enable audio playback from remote Bluetooth-connected devices](https://learn.microsoft.com/en-us/windows/apps/develop/media-playback/enable-remote-audio-playback)
- [AudioPlaybackConnection class](https://learn.microsoft.com/en-us/uwp/api/windows.media.audio.audioplaybackconnection?view=winrt-28000)
- [Enumerate devices](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/enumerate-devices)
- [Device information properties](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/device-information-properties)
- [DeviceWatcher class](https://learn.microsoft.com/en-us/uwp/api/windows.devices.enumeration.devicewatcher?view=winrt-28000)
- [BluetoothDevice class](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothdevice?view=winrt-28000)
- [Bluetooth Classic Audio](https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/bluetooth-classic-audio)
- [PhoneLineTransportDevice class](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.calls.phonelinetransportdevice?view=winrt-28000)
- [PhoneLineTransportDevice.RequestAccessAsync](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.calls.phonelinetransportdevice.requestaccessasync?view=winrt-28000)
- [Bluetooth GATT Client](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-client)
- [GattServiceUuids.Battery](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile.gattserviceuuids.battery?view=winrt-28000)
- [GattCharacteristicUuids.BatteryLevel](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile.gattcharacteristicuuids.batterylevel?view=winrt-28000)
