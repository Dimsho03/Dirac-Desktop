using ServiceLib.Manager;

namespace ServiceLib.Tests.Manager;

public class DiracDashboardStateTests
{
    [Test]
    public async Task SavedTunPreferenceIsNotMistakenForVerifiedConnection()
    {
        await (DiracDashboardState.Get(true, false, null, TimeSpan.FromSeconds(2))
               == EDiracDashboardState.Connecting).Should().BeTrue();
        await (DiracDashboardState.Get(true, true, null, TimeSpan.FromSeconds(2))
               == EDiracDashboardState.Connecting).Should().BeTrue();
        await (DiracDashboardState.Get(true, true, null, TimeSpan.FromSeconds(30))
               == EDiracDashboardState.Unavailable).Should().BeTrue();
    }

    [Test]
    public async Task ActiveNativeDiracMustHaveRunningCore()
    {
        await (DiracDashboardState.Get(true, false, true, TimeSpan.FromSeconds(1))
               == EDiracDashboardState.Connecting).Should().BeTrue();
        await (DiracDashboardState.Get(true, true, true, TimeSpan.FromSeconds(1))
               == EDiracDashboardState.Connected).Should().BeTrue();
        await (DiracDashboardState.Get(true, true, false, TimeSpan.FromSeconds(1))
               == EDiracDashboardState.Connected).Should().BeTrue();
    }

    [Test]
    public async Task ActualDiracConnectionRemainsVisibleUntilCoreHasStopped()
    {
        await (DiracDashboardState.Get(false, true, false, TimeSpan.Zero)
               == EDiracDashboardState.Disconnecting).Should().BeTrue();
        await (DiracDashboardState.Get(false, false, null, TimeSpan.Zero)
               == EDiracDashboardState.Disconnected).Should().BeTrue();
    }

    [Test]
    public async Task ForeignCoreDoesNotCountAsNativeDirac()
    {
        await (DiracDashboardState.Get(false, true, null, TimeSpan.Zero)
               == EDiracDashboardState.Disconnected).Should().BeTrue();
        await (DiracDashboardState.Get(true, true, null, TimeSpan.FromMinutes(2))
               == EDiracDashboardState.Unavailable).Should().BeTrue();
    }
}
