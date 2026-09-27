using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using ServiceLib.Manager;

namespace ServiceLib.Tests.Manager;

/// <summary>
/// Exercises the actual pinned Xray geosite/geoip rule engine, not merely the
/// generated JSON rule ordering. All egress is redirected to a local test
/// listener or a blackhole. No VPN tunnel, DNS changes or external traffic.
/// </summary>
public class DiracRussiaRoutingLoopbackTests
{
    [Test]
    public async Task PinnedXrayRoutesRussianDomainsToDirectAndProxyExceptionsToBlackhole()
    {
        var source = Environment.GetEnvironmentVariable("DIRAC_RU_PRIVATE_FIXTURE");
        var assetDir = Environment.GetEnvironmentVariable("DIRAC_RU_ASSET_DIR");
        var xray = Environment.GetEnvironmentVariable("DIRAC_RU_XRAY_EXE");
        if (string.IsNullOrWhiteSpace(source) &&
            string.IsNullOrWhiteSpace(assetDir) &&
            string.IsNullOrWhiteSpace(xray))
        {
            return; // Public test builds cannot access the private tested Xray.
        }
        if (string.IsNullOrWhiteSpace(source) ||
            string.IsNullOrWhiteSpace(assetDir) ||
            string.IsNullOrWhiteSpace(xray))
        {
            throw new InvalidOperationException("Incomplete offline Xray routing test environment.");
        }

        using var directListener = new TcpListener(IPAddress.Loopback, 0);
        directListener.Start();
        var directPort = ((IPEndPoint)directListener.LocalEndpoint).Port;

        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var socksPort = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();

        var temp = Path.Combine(Path.GetTempPath(), "dirac-ru-loopback-" + Guid.NewGuid().ToString("N") + ".json");
        Process? process = null;
        try
        {
            File.Copy(source, temp);
            await DiracRussiaRouting.ApplyFileAsync(temp, assetDir);
            var config = JsonNode.Parse(await File.ReadAllTextAsync(temp))!;

            // Preserve the real compiled RU rules, but replace all sources and
            // sinks with isolated localhost test endpoints; no private VLESS or TUN.
            config["inbounds"] = JsonNode.Parse($$"""
            [{
                "tag":"dirac-tun", "protocol":"socks", "listen":"127.0.0.1",
                "port":{{socksPort}}, "settings":{"auth":"noauth","udp":false}
            }]
            """);
            var outbounds = config["outbounds"]!.AsArray();
            outbounds[0] = JsonNode.Parse("""
            {
                "tag":"dirac-enc-test","protocol":"blackhole",
                "settings":{"response":{"type":"none"}}
            }
            """);
            outbounds[1]!["settings"] = new JsonObject
            {
                ["redirect"] = "127.0.0.1:" + directPort
            };

            config["log"] = new JsonObject { ["loglevel"] = "warning" };
            await File.WriteAllTextAsync(temp, config.ToJsonString());

            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = xray,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add("run");
            process.StartInfo.ArgumentList.Add("-config");
            process.StartInfo.ArgumentList.Add(temp);
            process.StartInfo.Environment["XRAY_LOCATION_ASSET"] = assetDir;
            if (!process.Start())
            {
                throw new InvalidOperationException("Pinned Xray offline routing fixture could not start.");
            }
            var stderr = process.StandardError.ReadToEndAsync();
            var stdout = process.StandardOutput.ReadToEndAsync();

            var connected = false;
            for (var attempt = 0; attempt < 40; attempt++)
            {
                using var probe = new TcpClient();
                try
                {
                    await probe.ConnectAsync(IPAddress.Loopback, socksPort);
                    connected = true;
                    break;
                }
                catch (SocketException)
                {
                    if (process.HasExited) throw new InvalidOperationException("Pinned offline Xray exited before SOCKS startup.");
                    await Task.Delay(75);
                }
            }
            await connected.Should().BeTrue();

            foreach (var name in new[]
                     {
                         "ozon.ru", "wildberries.ru", "wb.ru", "sberbank.ru",
                         "tbank.ru", "gosuslugi.ru", "yandex.ru", "vk.com",
                         "habr.com"
                     })
            {
                using var sock = await SocksRequestAsync(socksPort, name);
                using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                using var intercepted = await directListener.AcceptTcpClientAsync(limit.Token);
                await intercepted.Connected.Should().BeTrue();
                Console.WriteLine("RU_DIRECT_LOOPBACK_OK=" + name);
            }

            foreach (var name in new[] { "youtube.com", "openai.com", "discord.com", "telegram.org" })
            {
                using var sock = await SocksRequestAsync(socksPort, name);
                using var limit = new CancellationTokenSource(TimeSpan.FromMilliseconds(600));
                var redirected = false;
                try
                {
                    using var unexpected = await directListener.AcceptTcpClientAsync(limit.Token);
                    redirected = true;
                }
                catch (OperationCanceledException) when (limit.IsCancellationRequested)
                {
                }
                if (redirected) throw new InvalidOperationException("International blocked exception incorrectly used the direct loopback: " + name);
                Console.WriteLine("VPN_EXCEPTION_BLACKHOLE_OK=" + name);
            }

            if (process.HasExited)
            {
                throw new InvalidOperationException("Pinned Xray exited during offline route validation. " + await stderr);
            }
            Console.WriteLine("REAL_PINNED_XRAY_OFFLINE_ROUTE_CLASSIFICATION_PASS=true");
        }
        finally
        {
            if (process is not null)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
                using var exitDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await process.WaitForExitAsync(exitDeadline.Token);
                process.Dispose();
            }
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    private static async Task<TcpClient> SocksRequestAsync(int port, string destination)
    {
        var client = new TcpClient();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
            var stream = client.GetStream();
            await stream.WriteAsync(new byte[] { 5, 1, 0 }, timeout.Token);

            var answer = new byte[2];
            await stream.ReadExactlyAsync(answer, timeout.Token);
            if (answer[0] != 5 || answer[1] != 0)
            {
                throw new InvalidOperationException("Pinned Xray rejected loopback SOCKS5 greeting.");
            }

            var host = Encoding.ASCII.GetBytes(destination);
            if (host.Length > 255) throw new ArgumentOutOfRangeException(nameof(destination));
            var req = new byte[5 + host.Length + 2];
            req[0] = 5;
            req[1] = 1;
            req[2] = 0;
            req[3] = 3;
            req[4] = (byte)host.Length;
            host.CopyTo(req.AsSpan(5));
            req[5 + host.Length] = 1;
            req[6 + host.Length] = 187; // target 443
            await stream.WriteAsync(req, timeout.Token);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}