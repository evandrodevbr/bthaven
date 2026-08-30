# BTHaven

[![Phase 0](https://img.shields.io/badge/phase-0%20technical%20probes-0b6e99)](docs/probe-results.md)
[![Platform](https://img.shields.io/badge/platform-Windows%2011%20x64-0078d4)](docs/architecture.md)
[![License](https://img.shields.io/badge/license-MIT-16a34a)](LICENSE)

<p align="center">
  <img src="assets/branding/bthaven-horizontal.svg" alt="BTHaven logo" width="620">
</p>

**BTHaven** is an open-source, native Windows 11 Bluetooth device and audio hub. It is designed to make a Windows PC a practical local hub for phones, headsets, speakers, mice, and other Bluetooth devices.

The project is intentionally evidence-driven. Features that depend on restricted, undocumented, or profile-specific Windows behavior are preceded by executable probes. The HFP call path is a hard architecture gate: the application will not pretend to support phone calls until the Windows role and transport have been demonstrated with code and a real device.

> **Status:** Phase 1–3 groundwork is implemented. Device enumeration, layered battery providers, A2DP service state, endpoint inventory, HFP transport discovery, and Core Audio probes are available. The complete WinUI 3 shell and HFP call path remain gated by physical-device evidence.

## Scope

- real-time Bluetooth Classic/BLE device inventory;
- explicit paired / connected / present state separation;
- layered battery providers with `unavailable` as a valid result;
- Windows `AudioPlaybackConnection` A2DP sink investigation;
- WASAPI/Core Audio endpoint inventory and routing groundwork;
- HFP feasibility investigation with public API and capability evidence;
- `BluetoothDeviceManager`, Windows/GATT battery providers, `A2dpSinkService`, and WASAPI endpoint enumeration;
- local-only operation, structured logs, and privacy-preserving diagnostics.

The intended application stack is C# / .NET 10 / WinUI 3 / Windows App SDK, with C++/WinRT or Win32/COM isolated behind interfaces if the public managed surface is insufficient.

## Non-goals

BTHaven does not use screen mirroring, scrcpy, ADB, video streaming, cloud audio, analytics, a mandatory account, or a phone companion app as the primary design. Vendor-specific hacks, reverse engineering of Phone Link, HCI injection, and unsigned custom drivers are not introduced silently.

## Phase 0 gate

The first deliverable is evidence, not a UI mockup:

1. inspect the Windows build and SDK;
2. document A2DP, HFP, GATT battery, enumeration, and Core Audio assumptions;
3. build and execute the probes that the current machine supports;
4. record HRESULTs, API contracts, capabilities, and runtime limitations;
5. classify each path before implementing higher layers.

See:

- [`docs/bluetooth-profiles.md`](docs/bluetooth-profiles.md)
- [`docs/hfp-feasibility.md`](docs/hfp-feasibility.md)
- [`docs/probe-results.md`](docs/probe-results.md)
- [`docs/limitations.md`](docs/limitations.md)

## Build the Phase 0 probes

A per-user .NET SDK is sufficient; no system-wide SDK installation is required. With .NET 10 on `PATH`:

```powershell
dotnet restore BTHaven.slnx
dotnet build BTHaven.slnx -c Release
```

From Git Bash, if the SDK was installed per-user:

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet build BTHaven.slnx -c Release
```

Run the probes individually so their output can be inspected:

```text
dotnet run --project probes/01-device-enumeration -c Release -- --watch-seconds 5
dotnet run --project probes/02-battery -c Release
dotnet run --project probes/03-a2dp-sink -c Release
dotnet run --project probes/04-phone-hfp -c Release
dotnet run --project probes/05-call-audio-routing -c Release
```

The A2DP probe accepts `--device-id <id>` after the device list has been inspected. The HFP probe is discovery-only by default; access, registration, and connection attempts require explicit flags because they can change per-user Windows call integration state:

```text
dotnet run --project probes/04-phone-hfp -c Release -- --request-access --register --connect
```

Do not paste raw probe logs into public issues without removing device IDs, Bluetooth addresses, phone numbers, caller IDs, and endpoint names.

## Repository layout

```text
src/
 ├── BTHaven.App/          # WinUI 3 shell; created after the architecture gate
 ├── BTHaven.Core/         # UI-free domain models and contracts
 ├── BTHaven.Windows/      # Windows/WinRT/Core Audio adapters and services
 └── BTHaven.Native/       # optional native boundary, only when evidence requires it

tests/
 ├── BTHaven.Core.Tests/
 └── BTHaven.IntegrationTests/

probes/
 ├── Common/
 ├── 01-device-enumeration/
 ├── 02-battery/
 ├── 03-a2dp-sink/
 ├── 04-phone-hfp/
 └── 05-call-audio-routing/

docs/
 ├── architecture.md
 ├── bluetooth-profiles.md
 ├── capabilities.md
 ├── hfp-feasibility.md
 ├── limitations.md
 └── probe-results.md
```

## Privacy

BTHaven is local-first. Audio buffers are intended to remain in memory, call audio is not recorded, and no telemetry or audio upload path is part of the architecture.

## Contributing

Contributions should include a reproducible probe or a public documentation citation when they touch Windows Bluetooth roles, capabilities, audio transports, drivers, or packaging. A green build is not evidence that a Bluetooth profile is available at runtime.

## License

MIT. See [`LICENSE`](LICENSE).
