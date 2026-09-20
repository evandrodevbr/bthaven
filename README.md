# BTHaven

[![Phase 0](https://img.shields.io/badge/phase-0%20technical%20probes-0b6e99)](docs/probe-results.md)
[![Platform](https://img.shields.io/badge/platform-Windows%2011%20x64-0078d4)](docs/architecture.md)
[![License](https://img.shields.io/badge/license-MIT-16a34a)](LICENSE)

<p align="center">
  <img src="assets/branding/bthaven-horizontal.svg" alt="BTHaven logo" width="620">
</p>

**BTHaven** is an open-source, native Windows 11 Bluetooth device and audio hub. It is designed to make a Windows PC a practical local hub for phones, headsets, speakers, mice, and other Bluetooth devices.

The project is intentionally evidence-driven. Features that depend on restricted, undocumented, or profile-specific Windows behavior are preceded by executable probes. The HFP call path is a hard architecture gate: the application will not pretend to support phone calls until the Windows role and transport have been demonstrated with code and a real device.

> **Status:** The WinUI 3 shell, structured local logs, diagnostics export, device enumeration, layered battery providers, A2DP service, endpoint inventory, and controlled A2DP reconnect are implemented. On the development machine, the official A2DP target opens successfully; audible end-to-end playback still requires listening while phone media is actively routed to the PC. HFP transport discovery works, but access is denied by the restricted Windows capability and call audio remains blocked.

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

A per-user .NET SDK is sufficient; no system-wide SDK installation is required. In **Windows PowerShell**:

```powershell
Set-Location 'C:\Users\evand\Documents\GitHub\bthaven'
$env:Path = "$env:USERPROFILE\.dotnet;$env:Path"
& "$env:USERPROFILE\.dotnet\dotnet.exe" restore BTHaven.slnx
& "$env:USERPROFILE\.dotnet\dotnet.exe" build BTHaven.slnx -c Release -p:Platform=x64
```

From Git Bash, if the SDK was installed per-user:

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet build BTHaven.slnx -c Release -p:Platform=x64
```

## Run the WinUI app

The app is packaged with MSIX and must be launched with package identity. Do not start `BTHaven.App.exe` directly from `bin`; that bypasses package activation and can fail with `REGDB_E_CLASSNOTREG`.

Build first, then launch the existing output through the package-aware MSBuild target:

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build BTHaven.slnx -c Release -p:Platform=x64
& "$env:USERPROFILE\.dotnet\dotnet.exe" msbuild src/BTHaven.App/BTHaven.App.csproj -t:Run -p:Platform=x64 -p:Configuration=Release -p:WinAppRunDetach=true
```

The MSBuild target passes the generated build-output folder directly to `winapp.exe`. This avoids the .NET CLI project-mode fallback, which can fail when the Windows `dotnet` command is not discoverable by the launcher.

The first launch requires the Windows App Runtime packages for version `2.4.0` (framework, main, singleton, and DDLM) registered for the current user. The MSIX files are available under the restored `microsoft.windowsappsdk.runtime\2.4.0\tools\MSIX\win10-x64` NuGet directory.

The package-aware command registers a development identity and launches the app through its AUMID; the direct executable path is not a supported launch path for this project.

Close the running app before rebuilding; the registered AppX layout keeps `AppX\BTHaven.App.exe` open.

Run the probes individually so their output can be inspected:

```text
dotnet run --project probes/01-device-enumeration -c Release -- --watch-seconds 5
dotnet run --project probes/02-battery -c Release
dotnet run --project probes/03-a2dp-sink -c Release -- --exercise-first --hold-seconds 15
dotnet run --project probes/04-phone-hfp -c Release
dotnet run --project probes/05-call-audio-routing -c Release
```

The A2DP probe accepts `--device-id <id>` after the device list has been inspected, or `--exercise-first` to use the first ID returned by the official selector without shell escaping errors. The HFP probe is discovery-only by default; access, registration, and connection attempts require explicit flags because they can change per-user Windows call integration state:

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

Local logs under `%LOCALAPPDATA%\BTHaven\Logs` keep raw device identifiers so a failure can be correlated with a device, and they stay on the machine. The diagnostics ZIP is meant to be shared: it replaces each identity with a pseudonym (`redacted:<per-export token>:<sequence>`) that is random per export, so the same device maps to different names in two different ZIPs, while the service, RFCOMM, and protocol UUIDs needed to diagnose a profile failure are kept. See [`docs/architecture.md`](docs/architecture.md#diagnostics).

## Continuous integration

[`.github/workflows/ci.yml`](.github/workflows/ci.yml) restores with `--locked-mode`, builds the solution in `Release`/`x64`, and runs both test projects. The `packages.lock.json` files next to each project pin the resolved dependency graph; when a package reference changes, restore locally and commit the updated lock file with the change.

Tests that need real Bluetooth or audio hardware carry a `RequiresHardware` trait and are excluded from CI through `HARDWARE_TRAIT_FILTER`. Run them deliberately on a machine with the hardware attached:

```text
dotnet test tests/BTHaven.IntegrationTests -c Release
```

The SDK is pinned in [`global.json`](global.json). The language version, analysis level, and lock-file restore are set once in [`Directory.Build.props`](Directory.Build.props).

## Packaging and distribution

Signing uses a code-signing certificate in the local store; the private key never leaves it by default.

```powershell
# Creates or reuses a non-exportable signing certificate in CurrentUser\My and prints its thumbprint.
.\build\make-cert.ps1

# Sign the package by thumbprint — the csproj holds no pinned certificate.
& "$env:USERPROFILE\.dotnet\dotnet.exe" msbuild src/BTHaven.App/BTHaven.App.csproj -t:Publish `
  -p:Platform=x64 -p:Configuration=Release -p:AppxPackageSigningEnabled=true `
  -p:PackageCertificateThumbprint=<thumbprint>

# Assemble a shareable folder (must be a NEW directory; nothing existing is overwritten).
.\build\copy-to-desktop.ps1 -PackagePath '<path-to-signed-package>.msix' -Destination '<new-folder>'
```

The prepared folder contains the signed package, the public `.cer`, the installer, and signed dependencies — never a PFX. The end user runs `INSTALL.cmd`; only the certificate import elevates, and it asks for the full thumbprint before trusting the publisher in `LocalMachine\TrustedPeople`. `Root` is never modified. `.\build\check-packaging-safety.ps1` parses the active scripts and rejects a distribution folder that still carries private-key material.

Do not share `build/msix/AppPublisher.pfx` or any other PFX: it holds the private key that allows signing packages as this publisher, and every machine that trusts the certificate would accept them.



## Contributing

Contributions should include a reproducible probe or a public documentation citation when they touch Windows Bluetooth roles, capabilities, audio transports, drivers, or packaging. A green build is not evidence that a Bluetooth profile is available at runtime.

## License

MIT. See [`LICENSE`](LICENSE).
