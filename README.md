# Dirac Desktop

Dirac Desktop is an experimental Windows x64 VPN client derived from [v2rayN](https://github.com/2dust/v2rayN).

The project keeps the mature v2rayN desktop and service architecture while developing Dirac-specific networking, TUN, DNS bootstrap, update, and custom-core integration.

> Status: active development. There is no production-ready public release yet.

## Upstream

Dirac Desktop is based on the open-source [2dust/v2rayN](https://github.com/2dust/v2rayN) project.

The current Dirac codebase was started from the v2rayN 7.24.8 baseline and intentionally retains much of the upstream project structure, including the GlobalHotKeys submodule. Dirac-specific modifications are maintained in this repository.

If a problem reproduces in unmodified v2rayN, please also consult the upstream project and its documentation.

## Current scope

Dirac currently targets Windows x64.

Development includes:

- full IPv4 TUN mode;
- Dirac-specific DNS/DoH bootstrap logic;
- a pinned custom Xray runtime used by Dirac builds;
- controlled application updates based on Velopack;
- explicit separation between application updates and future profile-recovery logic.

The release pipeline is intentionally fail-closed: a Dirac update package must contain the expected pinned Xray and Wintun binaries. Private VPN profiles, credentials, UUIDs, keys, and user runtime configuration are not part of this repository.

## Build

Requirements:

- Git with submodule support;
- .NET 10 SDK;
- Windows for the current desktop target.

Clone with submodules, then build the desktop project:

    git clone --recurse-submodules https://github.com/Dimsho03/Dirac-Desktop.git
    cd Dirac-Desktop
    dotnet build v2rayN/v2rayN.Desktop/v2rayN.Desktop.csproj -c Release

A normal source build compiles the desktop application. Creating a distributable Dirac release additionally requires the pinned Dirac Xray/Wintun release inputs expected by scripts/Pack-Dirac-AppUpdate.ps1.

## Tests

ServiceLib tests are located in v2rayN/ServiceLib.Tests. The repository CI builds the Windows desktop target and runs the test suite on every relevant push or pull request.

## Application updates

The application-update foundation uses Velopack with separate stable and beta channels. Mutable application data is kept outside Velopack's replaceable application directory.

Design notes are in [docs/app-updates.md](docs/app-updates.md).

Profile/endpoint recovery is intentionally a separate subsystem and is not implemented by the application updater.

## Security and issue reports

Do not publish real VPN profiles, UUIDs, passwords, private keys, access tokens, cookies, or other credentials in issues, pull requests, logs, or test fixtures.

When reporting a problem, redact private endpoint data unless it is strictly necessary and safe to disclose.

## License

Dirac Desktop is distributed under the [GNU General Public License v3.0](LICENSE), consistent with its v2rayN-derived codebase.

This repository includes substantial code originating from [v2rayN](https://github.com/2dust/v2rayN). Copyright and license notices in upstream and third-party components remain applicable to those components.

## Acknowledgements

- [v2rayN](https://github.com/2dust/v2rayN) — upstream desktop client and architecture.
- [Xray-core](https://github.com/XTLS/Xray-core) — upstream core project used as the basis for Dirac's pinned custom runtime.
- [Velopack](https://github.com/velopack/velopack) — application packaging and update framework.