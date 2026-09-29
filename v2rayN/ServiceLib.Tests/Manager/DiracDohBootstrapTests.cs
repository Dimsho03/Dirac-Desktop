using System.Net;
using System.Text.Json.Nodes;
using ServiceLib.Manager;

namespace ServiceLib.Tests.Manager;

public class DiracDohBootstrapTests
{
    private static JsonNode Fixture() => JsonNode.Parse("""
    {
      "inbounds": [
        { "protocol":"tun", "settings": { "autoSystemRoutingTable": ["0.0.0.0/0"], "dns": ["127.0.0.1"] } },
        { "tag":"dirac-local-dns", "protocol":"dokodemo-door", "listen":"127.0.0.1", "port":53,
          "settings": { "address":"1.1.1.1", "network":"udp" } }
      ],
      "outbounds": [{
        "protocol":"vless",
        "settings": { "vnext": [{ "address":"edge.dimsho.top", "users":[{"encryption":"opaque-mlkem"}] }] },
        "streamSettings": {
          "network":"ws",
          "tlsSettings": { "serverName":"edge.dimsho.top", "echConfig":"opaque" },
          "wsSettings": { "path":"/assets/dirac-vlessenc-test.js" },
          "echAntiDPI":"tlsrec"
        }
      }]
    }
    """)!;

    [Test]
    public async Task RecognizesEmbeddedTunBeforeGuiTunToggleAndDoesNotMutateTheImportedProfile()
    {
        var original = Fixture();
        var serialized = original.ToJsonString();
        await DiracDohBootstrap.ContainsNativeTun(original).Should().BeTrue();
        await (original.ToJsonString() == serialized).Should().BeTrue();

        var temp = Path.Combine(Path.GetTempPath(), "dirac-tun-detect-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(temp, serialized);
            await (await DiracDohBootstrap.ContainsNativeTunFileAsync(temp)).Should().BeTrue();

            original["inbounds"]![0]!["protocol"] = "socks";
            await DiracDohBootstrap.ContainsNativeTun(original).Should().BeFalse();
            await File.WriteAllTextAsync(temp, original.ToJsonString());
            await (await DiracDohBootstrap.ContainsNativeTunFileAsync(temp)).Should().BeFalse();
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    [Test]
    public async Task ChangesOnlyAddressAndHost()
    {
        var node = Fixture();
        var original = node.DeepClone();
        await DiracDohBootstrap.IsEligible(node).Should().BeTrue();
        await DiracDohBootstrap.TryRewrite(node, IPAddress.Parse("104.21.59.15")).Should().BeTrue();
        await (node["outbounds"]![0]!["settings"]!["vnext"]![0]!["address"]!.GetValue<string>() == "104.21.59.15").Should().BeTrue();
        await (node["outbounds"]![0]!["streamSettings"]!["wsSettings"]!["host"]!.GetValue<string>() == "edge.dimsho.top").Should().BeTrue();
        node["outbounds"]![0]!["settings"]!["vnext"]![0]!["address"] = "edge.dimsho.top";
        node["outbounds"]![0]!["streamSettings"]!["wsSettings"]!.AsObject().Remove("host");
        await JsonNode.DeepEquals(node, original).Should().BeTrue();
    }

    [Test]
    public async Task SkipsOtherCustomProfiles()
    {
        var node = Fixture();
        node["outbounds"]![0]!["streamSettings"]!["wsSettings"]!["path"] = "/different";
        var original = node.ToJsonString();
        await DiracDohBootstrap.IsEligible(node).Should().BeFalse();
        await DiracDohBootstrap.TryRewrite(node, IPAddress.Parse("104.21.59.15")).Should().BeFalse();
        await (node.ToJsonString() == original).Should().BeTrue();
    }

    [Test]
    public async Task RejectsPrivateAndIPv6EdgeIPs()
    {
        foreach(var text in new[] {"127.0.0.1","10.0.0.1","192.168.0.1","172.16.0.1","1::1"})
        {
            var node = Fixture();
            await DiracDohBootstrap.TryRewrite(node, IPAddress.Parse(text)).Should().BeFalse();
            await DiracDohBootstrap.IsEligible(node).Should().BeTrue();
        }
    }
}