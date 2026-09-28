using ServiceLib.Manager;

namespace ServiceLib.Tests.Manager;

public class DiracProfileImportTests
{
    private static string Example => """
    {
      "inbounds": [
        {
          "tag": "dirac-tun",
          "protocol": "tun",
          "settings": {
            "autoSystemRoutingTable": ["0.0.0.0/0"],
            "autoOutboundsInterface": "auto",
            "dns": ["127.0.0.1"]
          }
        },
        {
          "tag": "dirac-local-dns",
          "protocol": "dokodemo-door",
          "listen": "127.0.0.1",
          "port": 53,
          "settings": {"address": "1.1.1.1", "network": "udp"}
        }
      ],
      "outbounds": [{
        "tag": "dirac-encrypted-vless",
        "protocol": "vless",
        "settings": {
          "vnext": [{
            "address": "edge.dimsho.top",
            "port": 443,
            "users": [{"id": "SYNTHETIC-NOT-A-REAL-UUID", "encryption": "synthetic-opaque-mlkem"}]
          }]
        },
        "streamSettings": {
          "network": "ws",
          "tlsSettings": {
            "serverName": "edge.dimsho.top",
            "echConfig": "synthetic-test-ech"
          },
          "wsSettings": {"path": "/assets/dirac-vlessenc-test.js"},
          "echAntiDPI": "tlsrec",
          "additionalSyntheticField": {"doNotNormalize": [1, true, "preserve"]}
        }
      }]
    }
    """;

    [Test]
    public async Task AcceptsOriginalFullTunJsonWithoutModifyingIt()
    {
        var raw = Encoding.UTF8.GetBytes(Example);
        var original = raw.ToArray();

        await DiracProfileImport.IsCompatible(raw).Should().BeTrue();
        await raw.SequenceEqual(original).Should().BeTrue();
    }

    [Test]
    public async Task AcceptsUtf8BomButRetainsOriginalBytes()
    {
        var raw = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(Example)).ToArray();
        var original = raw.ToArray();

        await DiracProfileImport.IsCompatible(raw).Should().BeTrue();
        await raw.SequenceEqual(original).Should().BeTrue();
    }

    [Test]
    public async Task RejectsMalformedUtf8InsideAnOtherwiseValidProfile()
    {
        var raw = Encoding.UTF8.GetBytes(Example);
        var marker = Encoding.UTF8.GetBytes("synthetic-test-ech");
        var offset = raw.AsSpan().IndexOf(marker);
        await (offset >= 0).Should().BeTrue();
        raw[offset] = 0xC3;  // Invalid leading UTF-8 byte, followed by ASCII.
        raw[offset + 1] = (byte)'(';

        await DiracProfileImport.IsCompatible(raw).Should().BeFalse();
    }

    [Test]
    public async Task RejectsMalformedAndIncompleteProfiles()
    {
        foreach (var invalid in new[]
        {
            "",
            "vless://synthetic-link",
            "{invalid",
            "{}",
            """{"outbounds":[{"protocol":"vless"}]}"""
        })
        {
            await DiracProfileImport.IsCompatible(Encoding.UTF8.GetBytes(invalid)).Should().BeFalse();
        }

        var oversized = new byte[DiracProfileImport.MaxProfileBytes + 1];
        await DiracProfileImport.IsCompatible(oversized).Should().BeFalse();
    }

    [Test]
    public async Task RefusesModifiedRuntimeEdgeAndWebSocketPath()
    {
        var source = JsonNode.Parse(Example)!;
        foreach (var change in new Action<JsonNode>[]
        {
            root => root["outbounds"]![0]!["settings"]!["vnext"]![0]!["address"] = "104.21.59.15",
            root => root["outbounds"]![0]!["streamSettings"]!["wsSettings"]!["path"] = "/other",
            root => root["inbounds"]![0]!["settings"]!["dns"] = new JsonArray("8.8.8.8")
        })
        {
            var mutated = source.DeepClone();
            change(mutated);
            await DiracProfileImport.IsCompatible(Encoding.UTF8.GetBytes(mutated.ToJsonString())).Should().BeFalse();
        }
    }

    [Test]
    public async Task RejectionsDoNotEchoPrivateProfileData()
    {
        var secret = "SYNTHETIC-SENSITIVE-MARKER-DO-NOT-ECHO";
        var invalid = Encoding.UTF8.GetBytes("""{"password":""" + JsonSerializer.Serialize(secret) + "}");

        await DiracProfileImport.IsCompatible(invalid).Should().BeFalse();
        await DiracProfileImport.UnsupportedProfileMessage.Contains(secret).Should().BeFalse();
    }
}
