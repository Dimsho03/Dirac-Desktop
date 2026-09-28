namespace ServiceLib.Manager;

/// <summary>
/// An explicit, side-effect-free view of the managed Dirac native TUN.
/// A saved EnableTun preference alone is never proof of a live VPN.
/// </summary>
public static class DiracDashboardState
{
    public static EDiracDashboardState Get(
        bool tunRequested,
        bool coreRunning,
        bool? activeDiracRussiaDirect,
        TimeSpan waiting)
    {
        var verified = coreRunning && activeDiracRussiaDirect.HasValue;
        if (verified)
        {
            return tunRequested
                ? EDiracDashboardState.Connected
                : EDiracDashboardState.Disconnecting;
        }

        if (!tunRequested)
        {
            return EDiracDashboardState.Disconnected;
        }

        return waiting >= TimeSpan.FromSeconds(25)
            ? EDiracDashboardState.Unavailable
            : EDiracDashboardState.Connecting;
    }
}

public enum EDiracDashboardState
{
    Disconnected,
    Connecting,
    Connected,
    Disconnecting,
    Unavailable
}
