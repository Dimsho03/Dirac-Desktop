using ServiceLib.Manager;

namespace ServiceLib.Tests.Manager;

public class DiracRouteModeTests
{
    private static JsonNode PreparedFixture() => JsonNode.Parse("""
    {
      "inbounds": [
        { "tag":"dirac-socks", "protocol":"socks", "port":10808 },
        {
          "tag":"dirac-tun", "protocol":"tun",
          "settings":{
            "autoSystemRoutingTable":["0.0.0.0/0"],
            "autoOutboundsInterface":"auto",
            "dns":["127.0.0.1"]
          }
        },
        {
          "tag":"dirac-local-dns", "protocol":"dokodemo-door",
          "listen":"127.0.0.1", "port":53,
          "settings":{"address":"1.1.1.1", "network":"udp"}
        }
      ],
      "outbounds": [
        {
          "tag":"dirac-enc-test", "protocol":"vless",
          "settings":{"vnext":[{"address":"104.21.59.15",
            "users":[{"encryption":"synthetic-opaque-mlkem", "id":"NOT-A-REAL-UUID"}]}]},
          "streamSettings":{
            "network":"ws",
            "tlsSettings":{"serverName":"edge.dimsho.top","echConfig":"synthetic-ech"},
            "wsSettings":{"host":"edge.dimsho.top","path":"/assets/dirac-vlessenc-test.js"},
            "echAntiDPI":"tlsrec"
          }
        }
      ],
      "routing":{
        "domainStrategy":"AsIs",
        "rules":[
          {"type":"field","inboundTag":["dirac-tun"],"outboundTag":"nonexistent-direct"},
          {"type":"field","inboundTag":["dirac-socks"],"outboundTag":"dirac-enc-test"}
        ]
      }
    }
    """)!;

    [Test]
    public async Task RussiaDirectDefaultsOnIncludingOlderConfigurationFiles()
    {
        var fresh = new TunModeItem();
        var fromOldJson = JsonSerializer.Deserialize<TunModeItem>("{}")!;
        var optedOut = JsonSerializer.Deserialize<TunModeItem>("""{"DiracRussiaDirect":false}""")!;

        await fresh.DiracRussiaDirect.Should().BeTrue();
        await fromOldJson.DiracRussiaDirect.Should().BeTrue();
        await optedOut.DiracRussiaDirect.Should().BeFalse();
        await JsonSerializer.Deserialize<TunModeItem>(
                JsonSerializer.Serialize(optedOut))!.DiracRussiaDirect.Should().BeFalse();
    }

    [Test]
    public async Task FullVpnPrependsUnconditionalDnsAndTunProxyRulesAndRetainsOriginalVless()
    {
        var config = PreparedFixture();
        var vlessBefore = config["outbounds"]![0]!.DeepClone();
        var oldRules = config["routing"]!["rules"]!.DeepClone();

        await DiracRouteMode.TryApplyFullVpn(config).Should().BeTrue();
        var rules = config["routing"]!["rules"]!.AsArray();

        await (rules.Count == 5).Should().BeTrue();
        await (rules[0]!["outboundTag"]!.GetValue<string>() == "dirac-enc-test").Should().BeTrue();
        await (rules[0]!["inboundTag"]![0]!.GetValue<string>() ==
            DiracRussiaRouting.DnsInboundTag).Should().BeTrue();
        await (rules[1]!["outboundTag"]!.GetValue<string>() ==
            DiracRussiaRouting.DirectOutboundTag).Should().BeTrue();
        await (rules[1]!["process"]!.AsArray().Any(x =>
            x!.GetValue<string>() == "RvControlSvc.exe")).Should().BeTrue();
        await (rules[2]!["outboundTag"]!.GetValue<string>() == "dirac-enc-test").Should().BeTrue();
        await (rules[2]!["inboundTag"]![0]!.GetValue<string>() ==
            DiracRussiaRouting.TunTag).Should().BeTrue();
        await JsonNode.DeepEquals(rules[3], oldRules[0]).Should().BeTrue();
        await JsonNode.DeepEquals(rules[4], oldRules[1]).Should().BeTrue();
        await (config["outbounds"]!.AsArray().Count == 2).Should().BeTrue();
        await JsonNode.DeepEquals(config["outbounds"]![0], vlessBefore).Should().BeTrue();
        await (config["outbounds"]![1]!["tag"]!.GetValue<string>() ==
            DiracRussiaRouting.DirectOutboundTag).Should().BeTrue();
        await (config["outbounds"]![1]!["protocol"]!.GetValue<string>() == "freedom").Should().BeTrue();
    }

    [Test]
    public async Task FullVpnDoesNotRequireRussiaGeodataAndNeverModifiesInputProfile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dirac-fullvpn-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "ephemeral.json");
            var original = PreparedFixture().ToJsonString();
            await File.WriteAllTextAsync(path, original);
            await DiracRouteMode.ApplyFileAsync(path, dir, russiaDirect: false);

            var result = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            await (result["routing"]!["rules"]![2]!["inboundTag"]![0]!.GetValue<string>() ==
                DiracRussiaRouting.TunTag).Should().BeTrue();
            await (result["routing"]!["rules"]![1]!["process"]!.AsArray().Any(x =>
                x!.GetValue<string>() == "RvControlSvc.exe")).Should().BeTrue();
            await (result["outbounds"]!.AsArray().Count == 2).Should().BeTrue();
            await (Directory.GetFiles(dir, "*.tmp").Length == 0).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public async Task FullVpnRejectsMalformedOrUnpreparedSourceWithoutMutation()
    {
        foreach (var mutate in new Action<JsonNode>[]
        {
            root => root["outbounds"]![0]!["streamSettings"]!["wsSettings"]!.AsObject().Remove("host"),
            root => root["inbounds"]![1]!["settings"]!["dns"] = new JsonArray("8.8.8.8"),
            root => root["outbounds"]![0]!["protocol"] = "trojan",
            root => root["routing"]!.AsObject().Remove("rules")
        })
        {
            var config = PreparedFixture();
            mutate(config);
            var before = config.DeepClone();
            await DiracRouteMode.TryApplyFullVpn(config).Should().BeFalse();
            await JsonNode.DeepEquals(config, before).Should().BeTrue();
        }
    }

    [Test]
    public async Task DefaultRussiaDirectStillUsesTheOriginalPinnedPolicy()
    {
        var config = PreparedFixture();
        // The fixture is deliberately not applied twice; both modes accept
        // the same eligible prepared input, and RU-direct adds one outbound.
        await DiracRussiaRouting.TryApply(config).Should().BeTrue();
        await (config["outbounds"]!.AsArray().Count == 2).Should().BeTrue();
        await (config["outbounds"]![1]!["tag"]!.GetValue<string>() ==
            DiracRussiaRouting.DirectOutboundTag).Should().BeTrue();
    }
}
