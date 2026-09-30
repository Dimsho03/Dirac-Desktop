using System.Net.NetworkInformation;

namespace ServiceLib.Manager;

/// <summary>
/// Avoid changing network adapters or DNS when another Dirac/Xray instance
/// already has a Windows TUN interface up. This is a conservative, read-only
/// preflight; it does not stop the other instance.
/// </summary>
public static class DiracTunExclusivity
{
    private static readonly string[] KnownTunNames =
        ["dirac_single_tun", "xray_tun", "wintunsingbox_tun"];

    public static bool HasConflict(bool currentInstanceOwnsTun, IEnumerable<string> activeInterfaceNames)
    {
        ArgumentNullException.ThrowIfNull(activeInterfaceNames);
        return !currentInstanceOwnsTun && activeInterfaceNames.Any(name =>
            KnownTunNames.Contains(name, StringComparer.OrdinalIgnoreCase));
    }

    public static bool IsForeignTunActive(bool currentInstanceOwnsTun)
    {
        if (!OperatingSystem.IsWindows() || currentInstanceOwnsTun)
        {
            return false;
        }

        try
        {
            return HasConflict(false, NetworkInterface.GetAllNetworkInterfaces()
                .Where(item => item.OperationalStatus == OperationalStatus.Up)
                .Select(item => item.Name));
        }
        catch (NetworkInformationException)
        {
            // Do not proceed with a destructive adapter reset when the
            // operating system cannot report whether an active TUN exists.
            return true;
        }
    }
}
