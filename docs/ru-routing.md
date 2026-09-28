# Russia-direct split routing for Dirac Desktop

Dirac's first Windows MVP uses Russia-direct for its own pinned, managed
native-Xray full-IPv4 TUN only. Imported Custom profiles and independent local
SOCKS/HTTP clients keep their original behavior. Only ephemeral generated
Xray JSON is changed, after direct-DoH bootstrap and before Windows DNS/TUN
settings are modified.

## Selecting the TUN mode

For an eligible, pinned Dirac native IPv4 TUN profile, **Russia-direct is on by
default**, including when upgrading an older app configuration that does not
yet contain the routing preference. The legacy desktop menu exposes **Dirac
TUN routing → Russia directly (default) / Full VPN (all TUN traffic)**. The
preference is stored in the user's persistent configuration; switching it
does not rewrite the original private connection profile.

When this managed TUN is already connected, a mode change uses the existing
serialized CoreManager reload. On every start, both modes run the direct-DoH
edge bootstrap and keep the Windows localhost DNS guard. Russia-direct
validates SHA-256-pinned GeoData before changing Windows DNS/TUN. Full VPN
instead places unconditional DNS-inbound and TUN-inbound VLESS rules ahead of
all existing rules, without adding a physical direct outbound. Existing
independent SOCKS/HTTP inbound rules are left in place. If the native TUN is
off, changing the preference only saves it for the next connection.

This switch only applies to an eligible Dirac custom profile using the pinned
Xray runtime; it does not silently apply to generic v2rayN subscriptions or
other cores. The redesigned Dirac UI will expose the same persisted setting.

The managed ServiceLib mode sequence Russia-direct → Full VPN →
Russia-direct was also exercised against an isolated real HOME Windows IPv4
TUN: a real Ozon TLS connection had an Xray-owned physical socket in both
Russia-direct runs and **no physical Ozon socket** in Full VPN, while foreign
HTTPS used the VPS in all three runs. The manager logged completion and a
graceful CoreStop, and a separate passive audit confirmed Xray/TUN stopped,
physical DNS and default route restored, and ordinary direct HTTPS working.
The test supervisor's final process-exit assertion returned an error despite
those independent checks; do not treat this as a crash/reboot recovery test.

## Routing priority

1. Localhost Dirac DNS relay via encrypted VLESS.
2. Private/LAN destinations directly through the physical NIC.
3. Essential Russian services directly, even if a third-party blocklist has
   a false positive: Ozon, Wildberries, Gosuslugi, banks, Yandex, VK, Mail.ru,
   Rutube, Kinopoisk, Avito, 2GIS and Habr.
4. Russia-only-inside domain category directly.
5. Telegram, YouTube, Discord, OpenAI, Google, Meta, Twitter via VLESS.
6. Russia-blocked domains, blocked IPs and Telegram IPs via VLESS.
7. category-ru domains and geoip:ru destinations directly.
8. Other native-TUN traffic via encrypted VLESS.

Routing rules are scoped to dirac-tun, except the separate DNS relay rule.
A freedom outbound called dirac-ru-direct handles direct connections, with
the native TUN setting autoOutboundsInterface=auto preventing routing loops.
Sniffing HTTP/TLS/QUIC uses routeOnly=true and does not alter destinations.
Encrypted ClientHello and app-owned DoH may hide domains, so GeoIP and manual
future overrides remain important. domainStrategy=AsIs is retained to avoid
a new internal DNS dependency during initial release.

## GeoData provenance, reproducibility and licensing

Source: https://github.com/runetfreedom/russia-v2ray-rules-dat

The upstream project is GPL-3.0 licensed and incorporates v2fly public
domain-list-community categories and other public Russian blocking sources.
The upstream authors retain credit for collecting and compiling these lists.
Dirac releases include this notice and GNU GPL-3.0 license text.

The reviewed source snapshot was downloaded from the upstream release branch
on 2026-09-27 UTC; original SHA-256:

- geoip.dat: 5A403626D9FAD0465DCD05932830444245679ADE8A6C3874F2CE2752185E78B6
- geosite.dat: 76FDBE01687A6CC7683B50C38CEEA84941458E8371D215918DAF555665A537CD

To avoid shipping the 92 MB originals, the deterministic stdlib-only
scripts/Prepare-DiracRussiaGeodata.py preserves only these complete binary
protobuf category messages:

- GeoIP: private, ru, ru-blocked, telegram.
- GeoSite: category-ru, ru-blocked, ru-available-only-inside,
  youtube, discord, openai, telegram, google, twitter, meta.

Pinned outputs:

- geoip.dat: 1,283,319 bytes; SHA-256:
  3FF5C8723894A880B4AF93E1B0436C39226A98FBB64B6CD42C184588087241A7
- geosite.dat: 1,649,835 bytes; SHA-256:
  9B03F2E7B978D524E437D49869569B74124B0D744A3721CB38CF7522188FBFE4

Regeneration command (original input and empty output directory required):

    python scripts/Prepare-DiracRussiaGeodata.py --source-dir ORIGINAL --output-dir EMPTY

The generator rejects changed source hashes or missing categories. The
runtime and Velopack packer independently require matching trimmed output
hashes. The two checked release-pinned GeoData files are committed in resources/ru-routing as GPL-3.0-attributed reproducible build inputs. The release packer uses this directory by default; optional -GeoDataSource permits an explicitly supplied identical hash-pinned snapshot. The packer copies both checked
files to the package's top-level bin directory. Xray reads them there using
XRAY_LOCATION_ASSET. Installed Velopack Dirac copies its versioned public
bin directory into the persistent user-data root at application startup.

## DNS, IPv6 and validation limits

The first version intentionally retains the known-good encrypted DNS relay
for all requests. Separate direct Russian DNS is a future change requiring
tests against the already proven DoH bootstrap and Windows DNS guard.

The preset assumes the physical connection is actually in Russia. Direct
traffic outside Russia will not acquire a Russian IP merely because it is
categorized as domestic.

Before broad distribution, verify real Russian ISP egress for Wildberries,
Gosuslugi, banks, Telegram, YouTube and blocked .ru domains. Also test UDP,
QUIC, opaque-IP apps, DNS leakage, switching Wi-Fi/Ethernet, crash recovery
and real global IPv6. An offline Xray run -test proves configuration validity,
not independently verified physical direct-route egress.