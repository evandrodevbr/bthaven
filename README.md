# BTHaven

**A native Windows 11 Bluetooth device and audio hub: device inventory, battery state, A2DP media playback from a paired phone, and an evidence-gated investigation of HFP call audio.**

[![License: MIT](https://img.shields.io/badge/license-MIT-16a34a)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512bd4)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%2011%20x64-0078d4)](docs/architecture.md)
[![UI](https://img.shields.io/badge/UI-WinUI%203-0b6e99)](src/BTHaven.App/README.md)
[![Phase 0](https://img.shields.io/badge/phase-0%20probes-6b7280)](docs/probe-results.md)

<p align="center">
  <img src="assets/branding/bthaven-horizontal.svg" alt="BTHaven logo" width="620">
</p>

## About

BTHaven makes a Windows 11 PC a practical local hub for Bluetooth devices: phones, headsets, speakers, mice and other peripherals. It keeps `paired`, `present` and `connected` as separate observations instead of collapsing them into one boolean, reports battery as `unavailable` rather than inventing a number, and drives the official Windows A2DP path so a phone can send media to the PC.

It is also deliberately conservative about what it claims. Anything that depends on restricted, undocumented or profile-specific Windows behavior is preceded by a small executable probe that records real HRESULTs and capability results instead of assumptions. The HFP (phone call) path is a hard architecture gate: the app calls the real API, shows the real result, and never presents call audio as working before access, registration, transport and a bidirectional call test actually pass.

Everything is local-first. There is no telemetry, no account, and no audio upload path.

## How it works

The solution is split into a UI-free domain core, Windows adapters, a WinUI 3 shell, and small executable probes:

```text
┌───────────────────────────────────────────────────────────┐
│ BTHaven.App                                               │
│ WinUI 3, tray, settings, diagnostics                      │
└───────────────────────┬───────────────────────────────────┘
                        │ interfaces / models
┌───────────────────────▼───────────────────────────────────┐
│ BTHaven.Core                                              │
│ Devices · Battery · Bluetooth · Audio · Calls · Contracts │
└───────────────┬─────────────────────────┬─────────────────┘
                │                         │
┌───────────────▼──────────────┐  ┌──────▼─────────────────┐
│ BTHaven.Windows              │  │ BTHaven.Native         │
│ WinRT · DeviceWatcher        │  │ C++/WinRT/Win32/COM    │
│ AudioPlaybackConnection      │  │ only when evidence     │
│ Core Audio / WASAPI          │  │ requires it            │
└──────────────────────────────┘  └────────────────────────┘
```

The core defines the seams (`IBluetoothDeviceService`, `IBatteryService`, `IMediaAudioSink`, `IPhoneTransport`, `ICallSession`, `IAudioEndpointService`, `IAudioRouter`, `IAudioProcessingPipeline`) without importing a single WinRT or WinUI type, so domain logic stays testable on any OS. `BTHaven.Native` is a reserved, currently empty boundary.

The two audio paths are intentionally distinct:

```text
phone (A2DP source) ──► Windows AudioPlaybackConnection ──► system render endpoint
phone (HFP AG)      ──► Windows call transport           (gated, currently denied)
PC microphone       ──► WASAPI capture                   (gated, currently denied)
```

Battery is resolved through a provider chain, and each provider is allowed to answer "no trustworthy value":

```text
Windows association properties  ──►  GATT Battery Service (0x180F / 0x2A19)  ──►  vendor provider (future, opt-in)
```

Device updates come from `DeviceWatcher` events, not from polling. Paired Classic and BLE endpoints that share a `System.Devices.Aep.ContainerId` are grouped into one logical device while the source transport is preserved for diagnostics.

See [`docs/architecture.md`](docs/architecture.md) for the full boundary description.

## Stack

| Layer | Technology |
|---|---|
| Language | C# (`LangVersion` latest, nullable reference types enabled) |
| Runtime / SDK | .NET 10 (`net10.0` for the core, `net10.0-windows10.0.26100.0` for Windows code) |
| Desktop UI | WinUI 3 / Windows App SDK 2.4.0, packaged with MSIX tooling |
| Tray integration | `H.NotifyIcon.WinUI` |
| Bluetooth / devices | `Windows.Devices.Bluetooth`, `Windows.Devices.Enumeration` (`DeviceWatcher`), GATT client |
| Audio | `Windows.Media.Audio.AudioPlaybackConnection` (A2DP sink), Core Audio / WASAPI via `NAudio` 2.2.1 |
| Calls (gated) | `Windows.ApplicationModel.Calls.PhoneLineTransportDevice` |
| Tests | xUnit 2.9.3 with `Microsoft.NET.Test.Sdk` 17.12.0 |
| Build orchestration | MSBuild via the .NET SDK; `BTHaven.slnx` solution file |
| Branding assets | SVG sources plus `tools/render_brand_assets.py` (Pillow) |

## Requirements

### Windows (application, probes, integration tests)

- Windows 11 build 22621 (22H2) or newer. Windows projects target `net10.0-windows10.0.26100.0` with `SupportedOSPlatformVersion` 10.0.22621.0; the packaged app's `TargetPlatformMinVersion` is 10.0.17763.0.
- .NET SDK 10.0.400 or newer. A per-user install is enough; a system-wide SDK or Visual Studio is not required. Verified with 10.0.401.
- Windows SDK 10.0.26100 targeting pack (`Microsoft.Windows.SDK.NET.Ref`), restored automatically from NuGet.
- Windows App SDK 2.4.0 runtime and WinUI 3 tooling for `BTHaven.App`, run as a packaged (MSIX) app.
- A Bluetooth adapter reporting Classic and/or Bluetooth LE support. Enumeration works without a paired device; battery, A2DP and HFP evidence requires real hardware.
- x64 is the configured solution platform (`BTHaven.slnx` declares only the `x64` platform).

### Cross-platform (core library and unit tests only)

- Any OS with the .NET 10 SDK. `src/BTHaven.Core` and `tests/BTHaven.Core.Tests` target plain `net10.0` and have no Windows dependency; both were built and executed on Linux x64 as part of this repository's verification.

## Quick start

### 1. Build and test the cross-platform core (no Windows required)

```bash
# requires .NET SDK 10.0.400+; a per-user install works:
#   curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir "$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"

dotnet build src/BTHaven.Core/BTHaven.Core.csproj -c Release
dotnet test  tests/BTHaven.Core.Tests/BTHaven.Core.Tests.csproj -c Release
```

Expected result: `BTHaven.Core` builds with 0 warnings and 0 errors, and the unit test project reports `Passed: 11, Failed: 0`.

### 2. Build on Windows

```powershell
# from the repository root
dotnet restore BTHaven.slnx
dotnet build   BTHaven.slnx -c Release -p:Platform=x64
dotnet test    BTHaven.slnx -c Release -p:Platform=x64
```

The same commands work from Git Bash. Running the application additionally requires the Windows App SDK runtime and the MSIX tooling that ships with the Windows SDK; see [Builds and deployment](#builds-and-deployment).

### 3. Run the probes on Windows

Run each probe individually so its structured output can be inspected:

```text
dotnet run --project probes/01-device-enumeration -c Release -- --watch-seconds 5
dotnet run --project probes/02-battery -c Release
dotnet run --project probes/03-a2dp-sink -c Release -- --exercise-first --hold-seconds 15
dotnet run --project probes/04-phone-hfp -c Release
dotnet run --project probes/05-call-audio-routing -c Release
```

`03-a2dp-sink` accepts `--device-id <id>` after the device list has been inspected, or `--exercise-first` to use the first ID returned by the official selector (avoids shell escaping problems). `04-phone-hfp` is discovery-only by default; operations that can change per-user Windows call integration state are opt-in:

```text
dotnet run --project probes/04-phone-hfp -c Release -- --request-access --register --connect
```

## Usage

### Probes (structured CLI evidence)

| Probe | What it observes |
|---|---|
| `01-device-enumeration` | snapshot and `DeviceWatcher` observations for Bluetooth Classic and LE |
| `02-battery` | Windows battery reports, association properties, GATT `0x180F`/`0x2A19` |
| `03-a2dp-sink` | `AudioPlaybackConnection` discovery plus optional open/hold/close exercise |
| `04-phone-hfp` | `PhoneLineTransportDevice` runtime, selector, access, registration, connect |
| `05-call-audio-routing` | Core Audio / WASAPI render and capture endpoint and format inventory |

Probes fail closed: a missing device, unavailable capability, denied access or unsupported profile is printed as such and never converted into a fake success.

### Application (WinUI 3, Windows only)

- real-time device list with filters: all, connected, paired, Bluetooth LE, Bluetooth Classic, audio, smartphones, peripherals;
- per-device detail panel: connection state, transport, address, `ContainerId`, battery value and source, observed capabilities;
- battery per provider chain, showing `unavailable` when no source is trustworthy;
- **media audio**: starts the official `AudioPlaybackConnection` path for the selected phone target, with the Windows output endpoint selector and an explicit warning when the selected endpoint is not the system default;
- controlled A2DP reconnect with a capped backoff schedule (1, 2, 5, 10, 30, 60 s);
- **phone calls**: a button that calls the real `PhoneLineTransportDevice` access/registration path and reports the actual system result;
- local structured logs and a diagnostics ZIP export that redacts sensitive fields before writing;
- tray icon with open / diagnostics / exit.

The application UI strings are currently Portuguese (pt-BR); repository documentation is English.

### Command line arguments

| Probe | Argument | Effect |
|---|---|---|
| `01-device-enumeration` | `--watch-seconds <n>` | how long to keep the `DeviceWatcher` observations open |
| `03-a2dp-sink` | `--device-id <id>` | exercise a specific `AudioPlaybackConnection` target |
| `03-a2dp-sink` | `--exercise-first` | use the first ID returned by `GetDeviceSelector()` |
| `03-a2dp-sink` | `--hold-seconds <n>` | how long to keep the opened connection alive |
| `04-phone-hfp` | `--request-access` | call `RequestAccessAsync()` |
| `04-phone-hfp` | `--register` | call `RegisterApp()` |
| `04-phone-hfp` | `--connect` | call `ConnectAsync()` after registration |

## Builds and deployment

- Build the whole solution: `dotnet build BTHaven.slnx -c Release -p:Platform=x64`.
- Build only the cross-platform core: `dotnet build src/BTHaven.Core/BTHaven.Core.csproj -c Release`.
- `BTHaven.App` is a packaged (MSIX) WinUI 3 application: `UseWinUI`, `EnableMsixTooling` and `SelfContained` are set, and `Microsoft.Windows.SDK.BuildTools.WinApp` adds `dotnet run` support with package identity. Publishing a distributable package is done through the Visual Studio "Package and Publish" path (or `msbuild` with the MSIX targets) on Windows; `Properties/PublishProfiles/` is not committed, so no publish profile is pinned by the repository.
- Runtime output never lands inside the repository: logs and diagnostics archives are written under `%LOCALAPPDATA%`.

## Project layout

```text
src/
 ├── BTHaven.App/          # WinUI 3 shell: device list, audio, calls, tray, diagnostics
 ├── BTHaven.Core/         # UI-free domain models, contracts and services (net10.0)
 ├── BTHaven.Windows/      # WinRT, DeviceWatcher, AudioPlaybackConnection, WASAPI adapters
 └── BTHaven.Native/       # reserved native boundary (README only, no project yet)

tests/
 ├── BTHaven.Core.Tests/         # unit tests for the cross-platform core
 └── BTHaven.IntegrationTests/   # live Windows machine tests (WinRT, WASAPI, A2DP discovery)

probes/
 ├── Common/               # probe arguments, logging and device property snapshots
 ├── 01-device-enumeration/
 ├── 02-battery/
 ├── 03-a2dp-sink/
 ├── 04-phone-hfp/
 └── 05-call-audio-routing/

docs/
 ├── architecture.md       # layer boundaries and contracts
 ├── bluetooth-profiles.md # profile map with live evidence
 ├── capabilities.md       # capability, packaging and permission classification
 ├── hfp-feasibility.md    # the call-audio gate and its result
 ├── limitations.md        # known limitations and non-assumptions
 └── probe-results.md      # evidence ledger from the development machine

assets/branding/           # SVG marks and lockups
tools/render_brand_assets.py  # regenerates the packaged PNG assets from the SVG language
```

## Verification

What exists:

- `tests/BTHaven.Core.Tests` — xUnit unit tests over the domain core (battery provider chain, battery state, device model, device projection/filtering, reconnect backoff).
- `tests/BTHaven.IntegrationTests` — Windows machine tests over the live `DeviceWatcher`, WASAPI endpoint enumeration and A2DP service discovery. They do not need a phone attached; positive Bluetooth media, battery and HFP results do require selected hardware.
- `probes/` — executable evidence for every restricted Windows path.

Verified on Linux x64 with .NET SDK 10.0.401:

```text
dotnet build src/BTHaven.Core/BTHaven.Core.csproj -c Release              -> 0 warnings, 0 errors
dotnet test  tests/BTHaven.Core.Tests/BTHaven.Core.Tests.csproj -c Release -> Passed: 11, Failed: 0
```

The Windows-targeted projects cannot be executed on Linux or macOS. With the .NET SDK on Linux they fail closed, which is the intended guard rail:

```text
dotnet build BTHaven.slnx -c Release -p:Platform=x64
  -> error NETSDK1100: To build a project targeting Windows on this operating system,
     set the EnableWindowsTargeting property to true.
```

Adding `-p:EnableWindowsTargeting=true` allows a compile-only pass on Linux: 10 of the 11 projects in the solution compile (core, Windows adapters, integration tests, probe common and all five probes). `BTHaven.App` still fails, because the WinUI 3 XAML compiler is a Windows binary:

```text
error : XamlCompiler output file ".../output.json" was not created. The XAML compiler may have crashed.
XamlCompiler.exe: cannot execute binary file: Exec format error
```

Compile-only success is not runtime evidence. Enumerating a device, opening A2DP, or reading a battery percentage requires Windows and real hardware.

## Status and limitations

The WinUI 3 shell, structured local logs, diagnostics export, device enumeration, layered battery providers, the A2DP service, endpoint inventory and controlled A2DP reconnect are implemented and build on Windows. Current probe results from the development machine are recorded in [`docs/probe-results.md`](docs/probe-results.md).

```text
DEVICE ENUMERATION: PASS    (paired Android phone observed; Classic endpoint connected)
BATTERY:            PARTIAL (Windows controller absent; association/GATT sources unavailable)
A2DP SINK:          PARTIAL (official target opened with Success; audible playback not yet listened to)
HFP PHONE LINK:     BLOCKED (transport found, access DeniedBySystem, RegisterApp 0x80070005)
HFP CALL AUDIO:     BLOCKED (registration/connect and bidirectional call test not available)
```

Known limitations:

- a device can be paired but absent, or present but disconnected; the UI keeps those states separate on purpose;
- battery is `unavailable` whenever no source exposes a trustworthy value, and no percentage is ever synthesized;
- A2DP: `OpenAsync()` returned `Success` for the paired phone, but that is transport evidence, not audible evidence. The public API routes to the Windows default render endpoint and exposes no arbitrary per-connection WASAPI endpoint selector. Audible end-to-end playback is still an open acceptance step;
- HFP: the runtime type and `CallsPhoneContract` v5 exist and a concrete Bluetooth transport is enumerated, but `RequestAccessAsync()` returns `DeniedBySystem` and `RegisterApp()` raises `UnauthorizedAccessException` (`HRESULT 0x80070005`). The restricted `phoneLineTransportManagement` capability is deliberately not declared in the normal manifest, so the app stays installable and the UI reports the real denial. Generic HFP Hands-Free Unit support for arbitrary phones is not proven;
- no AEC, noise suppression or AGC yet; the MVP prioritizes headsets over speakerphone;
- no vendor-specific battery hacks, no Phone Link reverse engineering, no HCI injection, no unsigned or virtual audio driver;
- probe results are machine- and device-dependent: a probe can prove an API path on one machine, not universal compatibility.

Raw probe and test output can contain device names, IDs, Bluetooth addresses, phone numbers and endpoint names. Redact those values before publishing logs in a public issue.

## Documentation

- [`docs/architecture.md`](docs/architecture.md) — layer boundaries, contracts and audio paths
- [`docs/bluetooth-profiles.md`](docs/bluetooth-profiles.md) — profile map with live evidence and official references
- [`docs/capabilities.md`](docs/capabilities.md) — capability, packaging and permission classification
- [`docs/hfp-feasibility.md`](docs/hfp-feasibility.md) — the call-audio gate, its current result and alternatives
- [`docs/limitations.md`](docs/limitations.md) — known limitations and non-assumptions
- [`docs/probe-results.md`](docs/probe-results.md) — the evidence ledger for the development machine
- [`probes/README.md`](probes/README.md) — probe index

## Contributing

Contributions touching Windows Bluetooth roles, capabilities, audio transports, drivers or packaging should include a reproducible probe or a public documentation citation. A green build is not evidence that a Bluetooth profile is available at runtime.

## License

MIT. See [`LICENSE`](LICENSE).
