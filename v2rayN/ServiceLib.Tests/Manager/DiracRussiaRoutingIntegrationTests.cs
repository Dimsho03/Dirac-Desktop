using System.Diagnostics;
using System.Text.Json.Nodes;
using ServiceLib.Manager;

namespace ServiceLib.Tests.Manager;

/// <summary>
/// Public CI runs the deterministic tests. An explicit private HOME environment
/// also exercises the real pinned Xray with a private ECH profile, without
/// putting credentials or generated configs into source control or test logs.
/// </summary>
public class DiracRussiaRoutingIntegrationTests
{
    [Test]
    public async Task PrivatePinnedGeodataPassesRealXrayConfigValidationWhenAvailable()
    {
        var source = Environment.GetEnvironmentVariable("DIRAC_RU_PRIVATE_FIXTURE");
        var assetDir = Environment.GetEnvironmentVariable("DIRAC_RU_ASSET_DIR");
        var xray = Environment.GetEnvironmentVariable("DIRAC_RU_XRAY_EXE");

        if (string.IsNullOrEmpty(source) && string.IsNullOrEmpty(assetDir) && string.IsNullOrEmpty(xray))
        {
            return;
        }
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(assetDir) || string.IsNullOrEmpty(xray))
        {
            throw new InvalidOperationException("Incomplete private Xray integration-test environment.");
        }

        var temp = Path.Combine(Path.GetTempPath(), "dirac-ru-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.Copy(source, temp);
            var original = JsonNode.Parse(await File.ReadAllTextAsync(temp))!;
            await DiracRussiaRouting.IsEligiblePrepared(original).Should().BeTrue();
            var originalVless = original["outbounds"]![0]!.DeepClone();

            await DiracRussiaRouting.ApplyFileAsync(temp, assetDir);
            var generated = JsonNode.Parse(await File.ReadAllTextAsync(temp))!;
            await JsonNode.DeepEquals(originalVless, generated["outbounds"]![0]).Should().BeTrue();
            await (generated["outbounds"]!.AsArray().Count == 2).Should().BeTrue();

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = xray,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            process.StartInfo.ArgumentList.Add("run");
            process.StartInfo.ArgumentList.Add("-test");
            process.StartInfo.ArgumentList.Add("-config");
            process.StartInfo.ArgumentList.Add(temp);
            process.StartInfo.Environment["XRAY_LOCATION_ASSET"] = assetDir;

            if (!process.Start())
            {
                throw new InvalidOperationException("Pinned test Xray could not start.");
            }

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(stdout, stderr);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("Pinned Xray rejected the generated Russia-routing config: exit " + process.ExitCode);
            }
            Console.WriteLine("DIRAC_RU_PINNED_XRAY_TEST_OK=true");
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }
}