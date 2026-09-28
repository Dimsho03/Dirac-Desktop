using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ServiceLib.Manager;

/// <summary>
/// Selects the routing policy for the eligible, pinned Dirac native IPv4 TUN.
/// Both policies run only after the same direct-DoH bootstrap and both retain
/// the Windows DNS guard. The imported private profile is never modified.
/// </summary>
public static class DiracRouteMode
{
    private static JsonObject InboundRule(string inbound, string outbound) => new()
    {
        ["type"] = "field",
        ["inboundTag"] = new JsonArray(JsonValue.Create(inbound)),
        ["outboundTag"] = outbound
    };

    /// <summary>
    /// Full VPN overrides every TUN and local-DNS rule before existing rules.
    /// Other (SOCKS/HTTP) inbounds retain their pre-existing routing rules.
    /// The original single VLESS outbound remains the only outbound.
    /// </summary>
    public static bool TryApplyFullVpn(JsonNode root)
    {
        if (!DiracRussiaRouting.IsEligiblePrepared(root))
        {
            return false;
        }

        var obj = root.AsObject();
        var outboundTag = obj["outbounds"]![0]!["tag"]!.GetValue<string>();
        var routing = obj["routing"]!.AsObject();
        var previousRules = routing["rules"]!.AsArray();
        var rules = new JsonArray
        {
            InboundRule(DiracRussiaRouting.DnsInboundTag, outboundTag),
            InboundRule(DiracRussiaRouting.TunTag, outboundTag)
        };

        // The first two rules are unconditional per-inbound matches: later
        // geosite/geoip/port rules cannot accidentally override full TUN VPN.
        foreach (var rule in previousRules)
        {
            if (rule is not null)
            {
                rules.Add(rule.DeepClone());
            }
        }

        routing["rules"] = rules;
        return true;
    }

    /// <summary>
    /// Transform only the generated ephemeral Xray JSON. The RU-direct branch
    /// additionally validates the pinned release geodata, fail-closed.
    /// </summary>
    public static async Task ApplyFileAsync(
        string generatedConfig,
        string assetDirectory,
        bool russiaDirect,
        CancellationToken cancellationToken = default)
    {
        if (russiaDirect)
        {
            await DiracRussiaRouting.ApplyFileAsync(generatedConfig, assetDirectory, cancellationToken);
            return;
        }

        var root = JsonNode.Parse(await File.ReadAllTextAsync(generatedConfig, cancellationToken))
            ?? throw new InvalidDataException("Generated Dirac Xray JSON is empty.");
        if (!TryApplyFullVpn(root))
        {
            throw new InvalidOperationException("Generated Xray config is not eligible for Dirac full VPN.");
        }

        var temporary = generatedConfig + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false),
                cancellationToken);
            File.Move(temporary, generatedConfig, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
