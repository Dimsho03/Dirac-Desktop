# Dirac Desktop application updates

Scope: Windows application binary updates only. Profile / endpoint recovery is intentionally separate.

## Decisions

- Velopack SDK and vpk are pinned to the same version: 1.2.158.
- Velopack startup handling runs first in Main and automatic apply-on-startup is disabled.
- Updates are applied explicitly so Dirac can save state and stop Xray/TUN before replacement.
- Installed builds keep mutable v2rayN/Dirac state outside Velopack's versioned current directory.
- Channels are architecture-specific: win-x64-stable and win-x64-beta.
- Dimsho.Dirac.Desktop is the initial package id. Stable/beta are channels, not separate installations.
- The backend accepts ordered update endpoints. It does not contain a GitHub token or any VPN profile data.
- GitHub can only be a client update source after releases are public; the current private repo must never cause a PAT to be embedded in Dirac.
- A static HTTPS mirror can be added as another endpoint later.
- Publishing is deliberately not automatic yet.
- Release packaging is fail-closed: the pack script requires the pinned custom Xray and signed Wintun hashes before Velopack can produce an installer/update feed.
- GitHub Actions publishing is intentionally deferred until the pinned custom-core dependency has a reproducible CI source; a workflow that silently omitted the custom core would produce a broken Dirac release.

## Apply lifecycle

1. Check one configured endpoint.
2. Download the package and let Velopack verify its feed checksum.
3. User confirms restart/update in the future UI.
4. Start Velopack's waiting updater.
5. Save Dirac state and stop Xray/TUN through AppManager.AppExitAsync.
6. Replace the installed version and restart Dirac.

## Before first public release

- Choose the public update origin (public GitHub release repository and/or static HTTPS mirror).
- Add UI for check/download/apply plus release notes and progress.
- Perform installed 0.1.0 -> 0.1.1 update and rollback/recovery tests.
- Confirm user data survives an update.
- Add Authenticode signing before broad distribution.
- Keep custom Xray pinned and excluded from upstream v2rayN core replacement.
- Installed Dirac data root: %LocalAppData%\Dirac; it is outside the Velopack current directory.
- The upstream v2rayN application updater is blocked in Dirac Desktop, including background app-update checks.
- The packer preserves prior releases in the feed directory so later versions can produce a continuous feed and delta packages.
