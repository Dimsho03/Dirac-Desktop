# Dirac Desktop profile import (MVP)

Dirac profiles are distributed by the owner. The application does not download,
subscribe to, or automatically update private connection profiles.

## Import a profile

In the current v2rayN-based desktop UI, open **Servers** and choose either:

- **Import Dirac profile from file...** to select the owner's complete `.json` file.
- **Import Dirac profile from clipboard** to paste the full, original Xray JSON text.

The dedicated Dirac importer copies the original JSON into private application
data without converting it to a `vless://` link or reserializing its encryption,
ECH or transport fields. File import leaves the source file unchanged. A
clipboard import preserves the pasted text as UTF-8.

Only the original supported Dirac native IPv4 TUN layout is accepted by this
dedicated importer. The original edge domain, WebSocket path, full-TUN route
and localhost DNS must match the current Dirac bootstrap prerequisites. This
is necessary to offer the bundled default Russia-direct routing.

Ordinary `vless://` links and other v2rayN formats remain available through
v2rayN's **Import from clipboard** action, but they do not automatically qualify
for Dirac's special pinned TUN / RU-direct path.

## Security and limitations

Never submit real profiles, UUIDs, secret encryption keys or passwords to
public issues, CI tests or source control. Validation errors deliberately do
not print the pasted contents. Avoid keeping plaintext profile files in
publicly synced folders or sharing them in unencrypted channels.

The first version does **not** provide automatic profile replacement,
subscriptions, an update URL or endpoint failover. The owner sends a new
profile when server parameters change.

The special route is applied only when the selected profile is eligible and
native TUN is enabled; importing a profile alone does not turn on the VPN.
For the currently tested HOME IPv4 backend, RU-direct is the default when
that route is activated. An explicit Full VPN switch belongs to the next UI
workstream.

The profile import code currently lives on `dirac-desktop/profile-import`
until validation and merge are complete.
