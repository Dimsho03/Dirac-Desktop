using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ServiceLib.Manager;

/// <summary>
/// Direct certificate-checked DoH preflight for SHA-pinned Dirac full IPv4 TUN.
/// Only the generated ephemeral Xray config is changed, never an imported profile.
/// </summary>
public static class DiracDohBootstrap
{
    public const string EdgeHost = "edge.dimsho.top";
    private static readonly IPAddress[] Resolvers = [IPAddress.Parse("1.1.1.1"), IPAddress.Parse("1.0.0.1")];

    private static bool Text(JsonNode? node, string expected)
        => node is JsonValue value && value.TryGetValue<string>(out var text)
            && string.Equals(text, expected, StringComparison.Ordinal);

    /// <summary>
    /// Identify an embedded native TUN independently of the GUI's TUN toggle.
    /// A custom profile may start its own TUN when v2rayN reloads the selected
    /// server on startup. That must not bypass explicit Connect and the DNS guard.
    /// </summary>
    public static bool ContainsNativeTun(JsonNode? root)
        => root is JsonObject && root["inbounds"] is JsonArray inbounds
            && inbounds.OfType<JsonObject>().Any(x => Text(x["protocol"], "tun"));

    public static async Task<bool> ContainsNativeTunFileAsync(string fileName, CancellationToken ct = default)
    {
        var root = JsonNode.Parse(await File.ReadAllTextAsync(fileName, ct));
        return ContainsNativeTun(root);
    }

    public static bool IsEligible(JsonNode? root)
    {
        if (root is not JsonObject || root["outbounds"] is not JsonArray outbounds || outbounds.Count != 1
            || outbounds[0] is not JsonObject outbound || !Text(outbound["protocol"], "vless")
            || outbound["settings"]?["vnext"] is not JsonArray vnext || vnext.Count != 1
            || vnext[0] is not JsonObject server || !Text(server["address"], EdgeHost)
            || outbound["streamSettings"] is not JsonObject stream || !Text(stream["network"], "ws")
            || !Text(stream["tlsSettings"]?["serverName"], EdgeHost)
            || stream["wsSettings"] is not JsonObject ws
            || !Text(ws["path"], "/assets/dirac-vlessenc-test.js")
            || ws.ContainsKey("host") || ws.ContainsKey("headers")
            || root["inbounds"] is not JsonArray inbounds) return false;

        var tun = inbounds.OfType<JsonObject>().Where(x => Text(x["protocol"], "tun")).ToArray();
        var dns = inbounds.OfType<JsonObject>().Where(x => Text(x["tag"], "dirac-local-dns")).ToArray();
        return tun.Length == 1 && dns.Length == 1
            && tun[0]["settings"]?["autoSystemRoutingTable"] is JsonArray routes && routes.Count == 1
            && Text(routes[0], "0.0.0.0/0")
            && tun[0]["settings"]?["dns"] is JsonArray nameservers && nameservers.Count == 1
            && Text(nameservers[0], "127.0.0.1")
            && Text(dns[0]["protocol"], "dokodemo-door")
            && Text(dns[0]["listen"], "127.0.0.1") && dns[0]["port"]?.GetValue<int>() == 53
            && Text(dns[0]["settings"]?["address"], "1.1.1.1")
            && Text(dns[0]["settings"]?["network"], "udp");
    }

    private static bool IsPublicIPv4(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var a = ip.GetAddressBytes();
        return a[0] != 0 && a[0] != 10 && a[0] != 127 && a[0] < 224
            && !(a[0] == 169 && a[1] == 254)
            && !(a[0] == 172 && a[1] is >= 16 and <= 31)
            && !(a[0] == 192 && a[1] == 168)
            && !(a[0] == 100 && a[1] is >= 64 and <= 127);
    }

    public static async Task<bool> IsEligibleFileAsync(string generatedConfig, CancellationToken ct = default)
    {
        var root = JsonNode.Parse(await File.ReadAllTextAsync(generatedConfig, ct));
        return root is not null && IsEligible(root);
    }
    public static bool TryRewrite(JsonNode root, IPAddress ip)
    {
        if (!IsPublicIPv4(ip) || !IsEligible(root)) return false;
        var outbound = root["outbounds"]![0]!;
        outbound["settings"]!["vnext"]![0]!["address"] = ip.ToString();
        outbound["streamSettings"]!["wsSettings"]!["host"] = EdgeHost;
        return true;
    }

    public static Task<bool> PrepareAsync(string generatedConfig, CancellationToken ct = default)
        => PrepareAsync(generatedConfig, skipReachabilityProbe: false, ct);

    public static async Task<bool> PrepareAsync(string generatedConfig, bool skipReachabilityProbe, CancellationToken ct = default)
    {
        var root = JsonNode.Parse(await File.ReadAllTextAsync(generatedConfig, ct))
            ?? throw new InvalidDataException("Generated Xray JSON is empty.");
        if (!IsEligible(root)) return false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(28));
        var ips = await ResolveDirectDoH(deadline.Token);
        IPAddress? reachable = skipReachabilityProbe ? ips.FirstOrDefault() : null;
        if (!skipReachabilityProbe)
        {
            foreach (var ip in ips)
            {
                using var tcp = new TcpClient(AddressFamily.InterNetwork);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                timeout.CancelAfter(TimeSpan.FromMilliseconds(1900));
                try { await tcp.ConnectAsync(ip, 443, timeout.Token); reachable = ip; break; }
                catch (OperationCanceledException) when (!deadline.IsCancellationRequested) { }
                catch (SocketException) { }
            }
        }
        if (reachable == null || !TryRewrite(root, reachable))
            throw new IOException("Dirac pre-TUN bootstrap returned no reachable public edge.");
        var temp = generatedConfig + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false), deadline.Token);
            File.Move(temp, generatedConfig, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return true;
    }

    private static async Task<IPAddress[]> ResolveDirectDoH(CancellationToken ct)
    {
        Exception? last = null;
        foreach (var resolver in Resolvers)
        {
            using var handler = new SocketsHttpHandler
            {
                UseProxy = false,
                AllowAutoRedirect = false,
                ConnectCallback = async (context, token) =>
                {
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(resolver, context.DnsEndPoint.Port), token);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch { socket.Dispose(); throw; }
                }
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(11) };
            using var req = new HttpRequestMessage(HttpMethod.Get,
                "https://cloudflare-dns.com/dns-query?name=edge.dimsho.top&type=A");
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-json"));
            try
            {
                using var res = await client.SendAsync(req, ct);
                res.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStreamAsync(ct));
                var payload = doc.RootElement;
                if (payload.GetProperty("Status").GetInt32() != 0
                    || !payload.TryGetProperty("Answer", out var answers)
                    || answers.ValueKind != JsonValueKind.Array)
                    throw new IOException("Direct DoH returned no successful A answer.");
                var ips = new List<IPAddress>();
                foreach (var answer in answers.EnumerateArray())
                    if (answer.GetProperty("type").GetInt32() == 1
                        && IPAddress.TryParse(answer.GetProperty("data").GetString(), out var ip)
                        && IsPublicIPv4(ip) && !ips.Contains(ip)) ips.Add(ip);
                if (ips.Count is < 1 or > 8) throw new IOException("DoH returned an unexpected A record set.");
                return ips.ToArray();
            }
            catch (Exception ex) when ((ex is HttpRequestException or IOException or JsonException or TaskCanceledException)
                && !ct.IsCancellationRequested) { last = ex; }
        }
        throw new IOException("Both direct DoH resolvers failed before TUN start.", last);
    }
}