namespace ServiceLib.Tests.Manager;

public class DiracPinnedCoreTests
{
    [Test]
    public async Task ValidMarkerMustMatchExactBinary()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dirac-pin-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var exe = Path.Combine(dir, "xray.exe");
            var original = Encoding.UTF8.GetBytes("dummy-pinned-core");
            File.WriteAllBytes(exe, original);
            File.WriteAllText(Path.Combine(dir, DiracPinnedCore.MarkerFileName), Convert.ToHexString(SHA256.HashData(original)));
            await DiracPinnedCore.IsPinned(dir).Should().BeTrue();
            await DiracPinnedCore.Matches(exe, dir).Should().BeTrue();
            File.AppendAllText(exe, "-changed");
            await DiracPinnedCore.Matches(exe, dir).Should().BeFalse();
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public async Task InvalidMarkerFailsClosedAndAbsentMarkerIsNotPinned()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dirac-pin-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var exe = Path.Combine(dir, "xray.exe");
            File.WriteAllText(exe, "placeholder");
            await DiracPinnedCore.IsPinned(dir).Should().BeFalse();
            await DiracPinnedCore.Matches(exe, dir).Should().BeFalse();
            File.WriteAllText(Path.Combine(dir, DiracPinnedCore.MarkerFileName), "invalid sha");
            await DiracPinnedCore.IsPinned(dir).Should().BeTrue();
            await DiracPinnedCore.Matches(exe, dir).Should().BeFalse();
        }
        finally { Directory.Delete(dir, true); }
    }
}
