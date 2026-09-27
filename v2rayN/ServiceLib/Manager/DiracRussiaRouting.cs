using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ServiceLib.Manager;

/// <summary>
/// Default Russia-direct split-routing preset for the pinned Dirac native Xray IPv4 TUN.
/// Edits only the generated, ephemeral config after the independent direct-DoH bootstrap;
/// it never changes the imported user's private VLESS profile.
/// </summary>
public static class DiracRussiaRouting
{
    public const string TunTag = "dirac-tun";
    public const string DnsInboundTag = "dirac-local-dns";
    public const string DirectOutboundTag = "dirac-ru-direct";

    // A minimal subset of the runetfreedom release snapshot; never accept rolling
    // unsigned geodata at runtime. Regeneration and provenance: scripts/Prepare-DiracRussiaGeodata.py.
    public const string GeoipSha256 = "3FF5C8723894A880B4AF93E1B0436C39226A98FBB64B6CD42C184588087241A7";
    public const string GeositeSha256 = "9B03F2E7B978D524E437D49869569B74124B0D744A3721CB38CF7522188FBFE4";

    public static readonly IReadOnlyList<string> CriticalRussianDomains =
    [
        "domain:ozon.ru", "domain:ozoncdn.com", "domain:ozonusercontent.com",
        "domain:wildberries.ru", "domain:wb.ru", "domain:wbstatic.net",
        "domain:wbstatic.ru", "domain:wildberries.net",
        "domain:gosuslugi.ru", "domain:nalog.gov.ru",
        "domain:sberbank.ru", "domain:sber.ru", "domain:tbank.ru",
        "domain:tinkoff.ru", "domain:tinkoffbank.ru", "domain:alfabank.ru",
        "domain:vtb.ru", "domain:psb.ru", "domain:raiffeisen.ru",
        "domain:yandex.ru", "domain:yandex.net", "domain:vk.com",
        "domain:vk.ru", "domain:mail.ru", "domain:ok.ru",
        "domain:rutube.ru", "domain:kinopoisk.ru", "domain:avito.ru",
        "domain:2gis.ru", "domain:2gis.com", "domain:habr.com"
    ];

    public static readonly IReadOnlyList<string> ExplicitProxyDomains =
    [
        "geosite:youtube", "geosite:discord", "geosite:openai",
        "geosite:telegram", "geosite:google", "geosite:twitter", "geosite:meta",
        "domain:googlevideo.com", "domain:discord.media"
    ];

    private static bool Text(JsonNode? node, string expected)
        => node is JsonValue value && value.TryGetValue<string>(out var text)
            && string.Equals(text, expected, StringComparison.Ordinal);

    private static JsonArray Strings(IEnumerable<string> items)
    {
        var arr = new JsonArray();
        foreach (var item in items)
        {
            arr.Add(item);
        }
        return arr;
    }

    private static JsonObject TunRule(string outbound, string property, IEnumerable<string> values)
        => new()
        {
            ["type"] = "field",
            ["inboundTag"] = Strings([TunTag]),
            ["outboundTag"] = outbound,
            [property] = Strings(values)
        };

    private static bool HasSingleString(JsonNode? node, string expected)
        => node is JsonArray arr && arr.Count == 1 && Text(arr[0], expected);

    /// <summary>
    /// The source configuration must already have been prepared by DiracDohBootstrap,
    /// which resolves the public edge before Windows' DNS is replaced with localhost.
    /// </summary>
    public static bool IsEligiblePrepared(JsonNode? root)
    {
        if (root is not JsonObject obj
            || obj["outbounds"] is not JsonArray outbounds || outbounds.Count != 1
            || outbounds[0] is not JsonObject outbound || !Text(outbound["protocol"], "vless")
            || outbound["tag"] is not JsonValue tag || !tag.TryGetValue<string>(out var tagValue)
            || string.IsNullOrWhiteSpace(tagValue) || tagValue == DirectOutboundTag
            || outbound["streamSettings"] is not JsonObject stream
            || !Text(stream["network"], "ws")
            || !Text(stream["tlsSettings"]?["serverName"], DiracDohBootstrap.EdgeHost)
            || !Text(stream["wsSettings"]?["host"], DiracDohBootstrap.EdgeHost)
            || !Text(stream["wsSettings"]?["path"], "/assets/dirac-vlessenc-test.js")
            || obj["inbounds"] is not JsonArray inbounds
            || obj["routing"] is not JsonObject routing || routing["rules"] is not JsonArray)
        {
            return false;
        }

        var tun = inbounds.OfType<JsonObject>().Where(x => Text(x["tag"], TunTag)).ToArray();
        var localDns = inbounds.OfType<JsonObject>().Where(x => Text(x["tag"], DnsInboundTag)).ToArray();
        return tun.Length == 1 && localDns.Length == 1
            && Text(tun[0]["protocol"], "tun")
            && HasSingleString(tun[0]["settings"]?["autoSystemRoutingTable"], "0.0.0.0/0")
            && HasSingleString(tun[0]["settings"]?["dns"], "127.0.0.1")
            && Text(tun[0]["settings"]?["autoOutboundsInterface"], "auto")
            && Text(localDns[0]["protocol"], "dokodemo-door")
            && Text(localDns[0]["listen"], "127.0.0.1")
            && localDns[0]["port"]?.GetValue<int>() == 53
            && Text(localDns[0]["settings"]?["address"], "1.1.1.1")
            && Text(localDns[0]["settings"]?["network"], "udp");
    }

    /// <summary>In-memory transformation of one already-prepared ephemeral config.</summary>
    public static bool TryApply(JsonNode root)
    {
        if (!IsEligiblePrepared(root))
        {
            return false;
        }

        var obj = root.AsObject();
        var outbounds = obj["outbounds"]!.AsArray();
        var mainTag = outbounds[0]!["tag"]!.GetValue<string>();
        var tun = obj["inbounds"]!.AsArray().OfType<JsonObject>()
            .Single(x => Text(x["tag"], TunTag));
        var routing = obj["routing"]!.AsObject();
        var original = routing["rules"]!.AsArray();

        // DNS queries from Windows must continue through the already validated
        // encrypted VLESS path, regardless of the destination IP geolocation.
        var rules = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "field",
                ["inboundTag"] = Strings([DnsInboundTag]),
                ["outboundTag"] = mainTag
            },
            TunRule(DirectOutboundTag, "ip", ["geoip:private"]),
            TunRule(DirectOutboundTag, "domain", CriticalRussianDomains),
            TunRule(DirectOutboundTag, "domain", ["geosite:ru-available-only-inside"]),
            TunRule(mainTag, "domain", ExplicitProxyDomains),
            TunRule(mainTag, "domain", ["geosite:ru-blocked"]),
            TunRule(mainTag, "ip", ["geoip:telegram", "geoip:ru-blocked"]),
            TunRule(DirectOutboundTag, "domain", ["geosite:category-ru"]),
            TunRule(DirectOutboundTag, "ip", ["geoip:ru"]),
            // A TUN-specific fallback prevents pre-existing "all TUN -> VLESS"
            // rules from stealing traffic from the higher-priority RU exceptions.
            new JsonObject
            {
                ["type"] = "field",
                ["inboundTag"] = Strings([TunTag]),
                ["outboundTag"] = mainTag
            }
        };

        // Keep routing for SOCKS/HTTP and any independent non-TUN inbound.
        foreach (var item in original)
        {
            if (item is not null)
            {
                rules.Add(item.DeepClone());
            }
        }

        // Retain the single pinned VLESS outbound untouched; add a local physical
        // network exit. The native TUN's autoOutboundsInterface=auto binds it to
        // the physical NIC rather than recapturing its own traffic in Wintun.
        outbounds.Add(new JsonObject
        {
            ["tag"] = DirectOutboundTag,
            ["protocol"] = "freedom",
            ["settings"] = new JsonObject()
        });

        var sniffing = tun["sniffing"] as JsonObject ?? new JsonObject();
        sniffing["enabled"] = true;
        sniffing["destOverride"] = Strings(["http", "tls", "quic"]);
        sniffing["routeOnly"] = true;
        tun["sniffing"] = sniffing;

        routing["domainStrategy"] = "AsIs";
        routing["rules"] = rules;
        return true;
    }

    private static async Task VerifyOneFileAsync(string path, string expected, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Dirac Russia routing requires pinned geodata before TUN starts.", path);
        }

        await using var file = File.OpenRead(path);
        if (file.Length is < 10000 or > 12000000)
        {
            throw new InvalidDataException("Unexpected trimmed Dirac geodata size.");
        }

        var actual = Convert.ToHexString(await SHA256.HashDataAsync(file, ct));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Dirac Russia geodata SHA-256 mismatch; refusing unverified routing rules.");
        }
    }

    /// <summary>
    /// Validate the release-pinned geodata in Xray's XRAY_LOCATION_ASSET directory
    /// before modifying the ephemeral core config. An invalid snapshot cannot
    /// change Windows routes/DNS or weaken the required direct-routing policy.
    /// </summary>
    public static async Task ApplyFileAsync(string generatedConfig, string assetDirectory, CancellationToken ct = default)
    {
        await VerifyOneFileAsync(Path.Combine(assetDirectory, "geoip.dat"), GeoipSha256, ct);
        await VerifyOneFileAsync(Path.Combine(assetDirectory, "geosite.dat"), GeositeSha256, ct);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(generatedConfig, ct))
            ?? throw new InvalidDataException("Generated Dirac Xray config is empty.");

        if (!TryApply(root))
        {
            throw new InvalidOperationException("Generated Dirac Xray config is not eligible for the Russia routing preset.");
        }

        var temp = generatedConfig + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false), ct);
            File.Move(temp, generatedConfig, true);
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