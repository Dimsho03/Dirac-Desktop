# Dirac Desktop: supervised Windows TUN checks and recovery

This document describes the source-only diagnostic and recovery scripts. An offline
test or a successful compilation is not proof of a working VPN. Keep an independent
connection to the HOME agent and the original Desktop emergency scripts while
testing the experimental client. Never publish imported private profile JSON or
full secret-bearing command lines as diagnostic artifacts.

## Lesson from the 2026-09-29 failed switch

The old Xray already owned UDP 127.0.0.1:53 and the dirac_single_tun adapter. Two
active copies cannot use the same localhost DNS socket. In a later supervised
switch, the guarded Xray started, UDP53 was owned, TUN was Up and the TUN default
route existed. That test aborted because it incorrectly required physical Wi-Fi
DNS 192.168.0.1 even while the guarded VPN was active. DiracWindowsDnsGuard
intentionally selects 127.0.0.1 while connected. Thus that test never proved
whether HTTPS through the new VPN actually worked. The external watchdog later
restarted the old Xray, but manual ordinary-network recovery was still needed.

## Two independent network health modes

The read-only scripts/Test-DiracNetwork.ps1 checks real network state and reports
one JSON object with Healthy and named Failures.

Connected mode requires ALL of: exact staged Xray process, that process owning
local UDP53, the chosen TUN adapter Up, its IPv4 default route present, every
protected physical interface using only 127.0.0.1 for IPv4 DNS, DNS resolution,
TCP to 1.1.1.1:443 and an HTTPS response. HTTPS is tested with Windows curl,
without a system proxy and without saving body or credentials.

Ordinary mode requires: the staged Xray has stopped, no experimental TUN/default
route remains, physical DNS matches the saved pre-switch baseline, and working
DNS, TCP443 and HTTPS. There is no hardcoded router DNS address. DHCP and custom
static DNS must both be preserved.

A successful TCP socket alone is NOT a successful HTTPS/TLS test.

## Before a future supervised switch

1. Confirm that the original VPN is working and that the emergency recovery
   script is saved locally. Verify the exact executable paths and the old Xray
   process by PID and path rather than stopping all xray.exe processes.
2. Before changing network state, capture a fresh physical DNS baseline with
   Test-DiracNetwork.ps1 using Mode Ordinary, CaptureBaselineOnly, and
   SaveBaselineFile pointing into a new isolated switch directory. Capture-only
   does not require the old VPN to be disconnected.
3. Register an independent one-shot SYSTEM watchdog using
   scripts/Manage-DiracRollbackWatchdog.ps1 with exact GuardedRoot,
   BaselineFile, ResultFolder and a unique TaskName starting with DiracRollback-.
   DelayMinutes defaults to 7 and accepts 5-15. Try WhatIf first; then use
   Confirm false from the elevated local controller. The task invokes
   Restore-DiracNetwork.ps1 independently of Euler. Confirm the scheduled task
   exists before stopping the old VPN. It skips restoration when success.ok
   or rollback-completed.ok is already present.
4. Disconnect the old VPN using its own manager. If an orphaned Xray has no
   controlling GUI, stop only its verified exact PID/path. Check ordinary
   connectivity using Test-DiracNetwork.ps1 in Ordinary mode with the saved
   BaselineFile and the staged ExpectedXrayExe. Do not proceed if it fails.
5. Launch only the isolated guarded GUI with the matching local runtime and
   its imported complete TUN profile. Wait for its actual window; a 20-second
   GUI timer is not a reliable crash detector. Click Connect once. Test
   Connected mode against the exact staged Xray path and TUN name.
6. Mark success.ok ONLY after connected-mode DNS, route, direct TCP AND
   HTTPS checks succeed. Then use Manage-DiracRollbackWatchdog.ps1 with
   Disarm to verify live network health again before unregistering the
   watchdog. Do not unregister it merely because a marker file exists.
   A graphical Connected label and active UDP53 are insufficient on their own.

## Scoped rollback contract

scripts/Restore-DiracNetwork.ps1 requires GuardedRoot, BaselineFile and
ResultFolder. It validates the snapshot and staged executable paths BEFORE
mutating anything, stops only the exact staged GUI and Xray, and restores DNS by
the original physical adapter GUID. When the guarded application's durable DNS
snapshot agrees with the pre-switch baseline, that snapshot is used. Otherwise
the immutable baseline is used. Static DNS is restored verbatim; a DHCP reset is
allowed only for an adapter originally recorded as DHCP. No global DNS reset.

An orphaned known TUN is disabled only if absolutely no Xray process remains,
so an unrelated live VPN adapter is not silently disconnected. Ordinary
network state, DNS and HTTPS must be verified before writing the
rollback-completed.ok marker. If verification fails, leave the recovery watchdog
armed and run the separately saved local emergency recovery procedure.

The old VPN is NOT automatically restarted by default. RestartPreviousVpn is
an explicit option requiring exact PreviousXrayExe and PreviousConfigFile paths,
and is attempted only after ordinary network verification. A restarted
process alone is not a verified VPN; perform another connected-mode test.

Rollback supports WhatIf. scripts/Test-DiracNetworkScripts.ps1 validates script
syntax and runs ten synthetic, offline checks, including rollback and watchdog WhatIf with
a synthetic baseline and dummy executables. It never changes adapters or DNS.

## Recovery ownership

Both manual and independent watchdog outcomes require a verified marker and a
follow-up live health check. A queued job, unavailable Euler connection, a
successful Xray start or TCP443 alone must not be reported as completed recovery.
Never deploy a source change to the user's active installation without a
separate staged build and end-to-end evidence.
