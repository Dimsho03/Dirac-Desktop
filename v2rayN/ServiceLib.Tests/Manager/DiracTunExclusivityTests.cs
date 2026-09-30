using ServiceLib.Manager;

namespace ServiceLib.Tests.Manager;

public class DiracTunExclusivityTests
{
    [Test]
    public async Task RefusesASecondDiracTunWhenAnExternalAdapterIsUp()
    {
        await DiracTunExclusivity.HasConflict(false, ["dirac_single_tun"]).Should().BeTrue();
        await DiracTunExclusivity.HasConflict(false, ["XrAy_TuN"]).Should().BeTrue();
        await DiracTunExclusivity.HasConflict(false, ["wintunsingbox_tun"]).Should().BeTrue();
    }

    [Test]
    public async Task AllowsOwnTunRestartAndIgnoresUnrelatedAdapters()
    {
        await DiracTunExclusivity.HasConflict(true, ["dirac_single_tun"]).Should().BeFalse();
        await DiracTunExclusivity.HasConflict(false, ["Wi-Fi", "Ethernet"]).Should().BeFalse();
        await DiracTunExclusivity.HasConflict(false, []).Should().BeFalse();
    }
}
