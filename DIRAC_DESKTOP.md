# Dirac Desktop

Windows VPN client based on [v2rayN](https://github.com/2dust/v2rayN) (Avalonia).

Status: development; not yet validated for end-user distribution.

- Upstream baseline: v2rayN tag 7.24.8.
- main: pinned upstream baseline.
- dirac-desktop/prep: Dirac-specific integration and verification.

This project uses a separately pinned modified Xray core. Private VPN profiles, credentials, keys, actual client runtime configurations, and staged binaries are not part of this repository.

Before a stable release: finish GUI-integrated VPN and TUN testing, DNS and IPv6 protection, crash recovery, packaging and secure updates. Preserve upstream and third-party license notices.