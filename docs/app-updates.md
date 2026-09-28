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
- The first client endpoint is now hardcoded to unauthenticated `https://github.com/Dimsho03/Dirac-Desktop` GitHub Releases; the active app update channel is `win-x64-stable` by default, with `win-x64-beta` by explicit user opt-in.
- The app-update beta choice is persisted independently of v2rayN core prerelease updates; no profile subscription or replacement is implied.
- The source repository `Dimsho03/Dirac-Desktop` was made **public on 2026-09-28**, following full Dirac commit/branch secret scans. Public GitHub Releases can be read without embedding a GitHub access token. **No application release has been published yet:** public source access alone does not provide an installable update.
- A static HTTPS mirror can be added as another endpoint later.
- Publishing is deliberately not automatic: `scripts/Publish-DiracGitHubRelease.ps1` validates local Velopack manifests, SHA-256 and private-payload exclusion by default without writing GitHub data. It only creates a **draft** when explicitly called with `-PublishDraft`, after the repo is public and the installer has a valid Authenticode signature.
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

- GitHub Releases is now the first app-update origin in the desktop code. The source repository is public, but first-release packaging and publication remain separate work. Do not embed a PAT. An independent mirror is deliberately deferred.
- The legacy Avalonia Help menu now offers tokenless GitHub check/download/apply with quarter-step progress and Stable/Beta choice. The redesigned UI still needs to add release notes, an explicit progress panel and a cancellation path.
- HOME successfully completed a separate installed 0.1.2 -> 0.1.3 offline Velopack delta update with restart, pinned runtime hashes and data persistence. A real *public GitHub-origin* installed update and rollback/recovery remain to be tested after the first public release.
- Confirm user data survives an update.
- Add Authenticode signing before broad distribution.
- Keep custom Xray pinned and excluded from upstream v2rayN core replacement.
- Installed Dirac data root: %LocalAppData%\Dirac; it is outside the Velopack current directory.
- The upstream v2rayN application updater is blocked in Dirac Desktop, including background app-update checks.
- The packer preserves prior releases in the feed directory so later versions can produce a continuous feed and delta packages.


## First public release procedure

The pinned packer produces channel-specific feed files, including
`assets.win-x64-stable.json`, `releases.win-x64-stable.json`,
`RELEASES-win-x64-stable`, the full package, optional delta, portable ZIP
and installer. Beta uses the corresponding `win-x64-beta` filenames.

First, package a release with `scripts/Pack-Dirac-AppUpdate.ps1` using
the original SHA-256-pinned Dirac Xray/Wintun runtime, pinned GeoData and
Velopack 1.2.158. Never put a user's profile, UUID, encryption keys or
`guiConfigs` in the release build directory.

Validate the release package **without uploading anything**:

```powershell
.\scripts\Publish-DiracGitHubRelease.ps1 -Version 0.1.4 -Channel stable -FeedDir C:\path\to\validated-feed
```

After verifying the public repository's source tag, the installer is Authenticode-signed,
the source commit matches the package, and the release notes have been
reviewed, upload a *draft*:

```powershell
.\scripts\Publish-DiracGitHubRelease.ps1 -Version 0.1.4 -Channel stable -FeedDir C:\path\to\validated-feed -NotesFile C:\path\to\notes.md -PublishDraft
```

Review the draft's feed JSON, installer, package hashes, notices, GPL source
tag and release notes on GitHub. Publishing the reviewed draft is a distinct
manual step. For beta, use `-Channel beta` and a matching prerelease version.
The publisher uses the operator's existing GitHub CLI session; **no GitHub
token is shipped with or embedded in Dirac**.

When TUN is connected, the current legacy UI allows downloading updates but
does not attempt an untested connected-TUN installation. It asks the user to
disconnect first; a later check can use the already downloaded update. The
core still stops through `AppExitAsync` when applying an approved update.
This conservative restriction can be lifted after active-TUN update QA.
