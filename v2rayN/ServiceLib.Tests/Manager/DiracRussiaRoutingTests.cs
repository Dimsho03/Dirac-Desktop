using System.Text.Json.Nodes;
using ServiceLib.Manager;

namespace ServiceLib.Tests.Manager;

public class DiracRussiaRoutingTests
{
    private static JsonNode Fixture() => JsonNode.Parse("""
    {
      "inbounds": [
        { "tag":"dirac-socks", "protocol":"socks", "port":10808 },
        {
          "tag":"dirac-tun",
          "protocol":"tun",
          "settings": {
            "autoSystemRoutingTable":["0.0.0.0/0"],
            "autoOutboundsInterface":"auto",
            "dns":["127.0.0.1"]
          },
          "sniffing": {
            "enabled":true,
            "destOverride":["http","tls"],
            "routeOnly":true
          }
        },
        {
          "tag":"dirac-local-dns",
          "protocol":"dokodemo-door",
          "listen":"127.0.0.1",
          "port":53,
          "settings": {"address":"1.1.1.1","network":"udp"}
        }
      ],
      "outbounds": [
        {
          "tag":"dirac-enc-test",
          "protocol":"vless",
          "settings": {
            "vnext":[{"address":"104.21.59.15","users":[{"encryption":"opaque-test-value"}]}]
          },
          "streamSettings": {
            "network":"ws",
            "tlsSettings":{"serverName":"edge.dimsho.top","echConfig":"opaque-test"},
            "wsSettings":{"host":"edge.dimsho.top","path":"/assets/dirac-vlessenc-test.js"},
            "echAntiDPI":"tlsrec"
          }
        }
      ],
      "routing": {
        "domainStrategy":"AsIs",
        "rules":[{"type":"field","inboundTag":["dirac-tun"],"outboundTag":"dirac-enc-test"}]
      }
    }
    """)!;

    private static bool ArrayContains(JsonNode? node, string value)
        => node is JsonArray arr && arr.Any(x => x?.GetValue<string>() == value);

    [Test]
    public async Task CriticalRussianServicesPrecedeBlockListsAndRemainDirect()
    {
        var config = Fixture();
        var originalVless = config["outbounds"]![0]!.DeepClone();
        await DiracRussiaRouting.TryApply(config).Should().BeTrue();

        var rules = config["routing"]!["rules"]!.AsArray();
        var direct = DiracRussiaRouting.DirectOutboundTag;
        await (rules[0]!["outboundTag"]!.GetValue<string>() == "dirac-enc-test").Should().BeTrue();
        await ArrayContains(rules[0]!["inboundTag"], "dirac-local-dns").Should().BeTrue();

        var mustBeDirect = new[]
        {
            "domain:ozon.ru", "domain:ozoncdn.com", "domain:wildberries.ru",
            "domain:wb.ru", "domain:wbstatic.net", "domain:sberbank.ru",
            "domain:sber.ru", "domain:tbank.ru", "domain:tinkoff.ru",
            "domain:alfabank.ru", "domain:vtb.ru", "domain:gosuslugi.ru",
            "domain:nalog.gov.ru", "domain:yandex.ru", "domain:vk.com"
        };
        foreach (var domain in mustBeDirect)
        {
            await ArrayContains(rules[2]!["domain"], domain).Should().BeTrue();
        }
        await (rules[2]!["outboundTag"]!.GetValue<string>() == direct).Should().BeTrue();
        await ArrayContains(rules[3]!["domain"], "geosite:ru-available-only-inside").Should().BeTrue();
        await ArrayContains(rules[5]!["domain"], "geosite:ru-blocked").Should().BeTrue();
        await ArrayContains(rules[7]!["domain"], "geosite:category-ru").Should().BeTrue();
        await ArrayContains(rules[8]!["ip"], "geoip:ru").Should().BeTrue();
        await (rules[9]!["outboundTag"]!.GetValue<string>() == "dirac-enc-test").Should().BeTrue();

        await (config["outbounds"]!.AsArray().Count == 2).Should().BeTrue();
        await JsonNode.DeepEquals(config["outbounds"]![0], originalVless).Should().BeTrue();
        await (config["outbounds"]![1]!["protocol"]!.GetValue<string>() == "freedom").Should().BeTrue();
        await (config["outbounds"]![1]!["tag"]!.GetValue<string>() == direct).Should().BeTrue();
    }

    [Test]
    public async Task TelegramBlockedIPsAndInternationalServicesPrecedeRuGeoip()
    {
        var config = Fixture();
        await DiracRussiaRouting.TryApply(config).Should().BeTrue();

        var rules = config["routing"]!["rules"]!.AsArray();
        foreach (var name in new[] {"geosite:telegram","geosite:youtube","geosite:discord","geosite:openai"})
        {
            await ArrayContains(rules[4]!["domain"], name).Should().BeTrue();
        }
        await (rules[4]!["outboundTag"]!.GetValue<string>() == "dirac-enc-test").Should().BeTrue();
        await ArrayContains(rules[6]!["ip"], "geoip:telegram").Should().BeTrue();
        await ArrayContains(rules[6]!["ip"], "geoip:ru-blocked").Should().BeTrue();
        await (rules[6]!["outboundTag"]!.GetValue<string>() == "dirac-enc-test").Should().BeTrue();
        await (rules[8]!["outboundTag"]!.GetValue<string>() == DiracRussiaRouting.DirectOutboundTag).Should().BeTrue();
        await ArrayContains(rules[8]!["ip"], "geoip:ru").Should().BeTrue();
    }

    [Test]
    public async Task RoutingIsRestrictedToTunAndSniffingDoesNotChangeDestination()
    {
        var config = Fixture();
        await DiracRussiaRouting.TryApply(config).Should().BeTrue();
        var rules = config["routing"]!["rules"]!.AsArray();

        for (var i = 1; i <= 9; i++)
        {
            await ArrayContains(rules[i]!["inboundTag"], DiracRussiaRouting.TunTag).Should().BeTrue();
        }
        await ArrayContains(rules[10]!["inboundTag"], DiracRussiaRouting.TunTag).Should().BeTrue();
        var sniffing = config["inbounds"]![1]!["sniffing"]!;
        await sniffing["routeOnly"]!.GetValue<bool>().Should().BeTrue();
        await ArrayContains(sniffing["destOverride"], "http").Should().BeTrue();
        await ArrayContains(sniffing["destOverride"], "tls").Should().BeTrue();
        await ArrayContains(sniffing["destOverride"], "quic").Should().BeTrue();
        await (config["routing"]!["domainStrategy"]!.GetValue<string>() == "AsIs").Should().BeTrue();
    }

    [Test]
    public async Task OtherConfigsNeverChangeAndRepeatedApplyRefusesMutation()
    {
        foreach (var mutation in new Action<JsonNode>[]
        {
            x => x["outbounds"]![0]!["streamSettings"]!["wsSettings"]!["host"] = "another.example",
            x => x["inbounds"]![1]!["settings"]!["autoOutboundsInterface"] = "missing",
            x => x["inbounds"]![1]!["settings"]!["autoSystemRoutingTable"] = new JsonArray(),
            x => x["inbounds"]![2]!["listen"] = "0.0.0.0",
            x => x["outbounds"]![0]!["protocol"] = "trojan"
        })
        {
            var config = Fixture();
            mutation(config);
            var before = config.DeepClone();
            await DiracRussiaRouting.TryApply(config).Should().BeFalse();
            await JsonNode.DeepEquals(config, before).Should().BeTrue();
        }

        var good = Fixture();
        await DiracRussiaRouting.TryApply(good).Should().BeTrue();
        var applied = good.DeepClone();
        await DiracRussiaRouting.TryApply(good).Should().BeFalse();
        await JsonNode.DeepEquals(good, applied).Should().BeTrue();
    }

    [Test]
    public async Task RejectsUntrustedGeodataBeforeRewritingGeneratedConfig()
    {
        var path = Path.Combine(Path.GetTempPath(), "Dirac-RU-Routing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            var config = Fixture().ToJsonString();
            var configPath = Path.Combine(path, "generated.json");
            await File.WriteAllTextAsync(configPath, config);
            await File.WriteAllTextAsync(Path.Combine(path, "geoip.dat"), "wrong");
            await File.WriteAllTextAsync(Path.Combine(path, "geosite.dat"), "wrong");

            var threw = false;
            try
            {
                await DiracRussiaRouting.ApplyFileAsync(configPath, path);
            }
            catch (InvalidDataException)
            {
                threw = true;
            }

            await threw.Should().BeTrue();
            await (await File.ReadAllTextAsync(configPath) == config).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }
}