using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using CliWrap;
using CliWrap.Buffered;
using Microsoft.Win32;

namespace ServiceLib.Manager;

/// <summary>
///     Prevents Windows from bypassing the Dirac localhost DNS relay while the native Xray TUN is active.
///     A durable snapshot is written before any adapter is changed so normal stop/reload can restore DHCP/static DNS.
/// </summary>
public static class DiracWindowsDnsGuard
{
    public const string LoopbackDns = "127.0.0.1";
    private const string StateFileName = "dirac-dns-guard-state.json";

    private sealed class GuardState
    {
        public List<AdapterSnapshot> Adapters { get; set; } = [];
    }

    private sealed class AdapterSnapshot
    {
        public string InterfaceId { get; set; } = string.Empty;
        public int InterfaceIndex { get; set; }
        public bool DhcpDns { get; set; }
        public List<string> DnsServers { get; set; } = [];
    }

    private static string StatePath => Utils.GetBinConfigPath(StateFileName);

    /// <summary>
    /// True when a saved snapshot still needs restoration. A missing physical
    /// adapter can leave a pending entry without throwing from RestoreAsync.
    /// Never report "DNS restored" while this durable file remains.
    /// </summary>
    public static bool HasPendingRestore => Utils.IsWindows() && File.Exists(StatePath);

    public static async Task<bool> ApplyAsync()
    {
        if (!Utils.IsWindows())
        {
            return false;
        }

        if (!Utils.IsAdministrator())
        {
            throw new InvalidOperationException("Dirac DNS guard requires administrator rights in Windows TUN mode.");
        }

        await RestoreAsync();
        if (File.Exists(StatePath))
        {
            throw new InvalidOperationException("A previous Dirac DNS guard could not be fully restored.");
        }

        var snapshots = CaptureAdapters();
        if (snapshots.Count == 0)
        {
            throw new InvalidOperationException("No active physical IPv4 interface with a default gateway was found.");
        }

        var state = new GuardState { Adapters = snapshots };
        var stateDirectory = Path.GetDirectoryName(StatePath);
        if (!stateDirectory.IsNullOrEmpty())
        {
            Directory.CreateDirectory(stateDirectory);
        }

        var tempPath = StatePath + ".tmp";
        await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(state));
        File.Move(tempPath, StatePath, true);

        try
        {
            foreach (var snapshot in snapshots)
            {
                await SetServerAddressesAsync(snapshot.InterfaceIndex, [LoopbackDns]);
            }

            await FlushDnsCacheAsync();
            return true;
        }
        catch
        {
            try
            {
                await RestoreAsync();
            }
            catch (Exception restoreException)
            {
                Logging.SaveLog("DiracWindowsDnsGuard restore after apply failure", restoreException);
            }

            throw;
        }
    }

    /// <summary>
    ///     Reapplies the loopback DNS setting from the durable snapshot without replacing that snapshot.
    ///     Xray/Wintun may refresh interface networking while the TUN device is starting, so the guard is
    ///     asserted once before startup and once again immediately after the core is confirmed running.
    /// </summary>
    public static async Task ReassertAsync()
    {
        if (!Utils.IsWindows())
        {
            return;
        }

        if (!Utils.IsAdministrator())
        {
            throw new InvalidOperationException("Dirac DNS guard requires administrator rights in Windows TUN mode.");
        }

        if (!File.Exists(StatePath))
        {
            throw new InvalidOperationException("Dirac DNS guard state is missing; refusing to continue unguarded.");
        }

        GuardState? state;
        try
        {
            state = JsonSerializer.Deserialize<GuardState>(await File.ReadAllTextAsync(StatePath));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Dirac DNS guard state could not be read.", ex);
        }

        if (state?.Adapters is not { Count: > 0 })
        {
            throw new InvalidOperationException("Dirac DNS guard state contains no protected adapters.");
        }

        var failures = new List<Exception>();
        foreach (var snapshot in state.Adapters)
        {
            var current = FindInterface(snapshot.InterfaceId);
            if (current is null)
            {
                failures.Add(new InvalidOperationException(
                    $"Protected interface {snapshot.InterfaceId} disappeared while Dirac TUN was starting."));
                continue;
            }

            try
            {
                var index = current.GetIPProperties().GetIPv4Properties()?.Index ?? snapshot.InterfaceIndex;
                await SetServerAddressesAsync(index, [LoopbackDns]);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        if (failures.Count > 0)
        {
            throw new AggregateException("Windows DNS guard could not be reasserted after TUN startup.", failures);
        }

        await FlushDnsCacheAsync();
    }

    public static async Task RestoreAsync()
    {
        if (!Utils.IsWindows() || !File.Exists(StatePath))
        {
            return;
        }

        GuardState? state;
        try
        {
            state = JsonSerializer.Deserialize<GuardState>(await File.ReadAllTextAsync(StatePath));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Dirac DNS guard state could not be read.", ex);
        }

        if (state?.Adapters is not { Count: > 0 })
        {
            File.Delete(StatePath);
            return;
        }

        var pending = new List<AdapterSnapshot>();
        var failures = new List<Exception>();
        var changed = false;

        foreach (var snapshot in state.Adapters)
        {
            var current = FindInterface(snapshot.InterfaceId);
            if (current is null)
            {
                pending.Add(snapshot);
                continue;
            }

            try
            {
                var index = current.GetIPProperties().GetIPv4Properties()?.Index ?? snapshot.InterfaceIndex;
                if (snapshot.DhcpDns)
                {
                    await ResetServerAddressesAsync(index);
                }
                else
                {
                    if (snapshot.DnsServers.Count == 0)
                    {
                        throw new InvalidOperationException($"Static DNS snapshot for interface {snapshot.InterfaceId} is empty.");
                    }

                    await SetServerAddressesAsync(index, snapshot.DnsServers);
                }

                changed = true;
            }
            catch (Exception ex)
            {
                pending.Add(snapshot);
                failures.Add(ex);
            }
        }

        if (changed)
        {
            await FlushDnsCacheAsync();
        }

        if (pending.Count == 0)
        {
            File.Delete(StatePath);
        }
        else
        {
            var tempPath = StatePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(new GuardState { Adapters = pending }));
            File.Move(tempPath, StatePath, true);
        }

        if (failures.Count > 0)
        {
            throw new AggregateException("One or more Windows DNS interfaces could not be restored.", failures);
        }
    }

    private static List<AdapterSnapshot> CaptureAdapters()
    {
        var result = new List<AdapterSnapshot>();

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (!IsCandidate(networkInterface))
            {
                continue;
            }

            var properties = networkInterface.GetIPProperties();
            var ipv4 = properties.GetIPv4Properties();
            if (ipv4 is null)
            {
                continue;
            }

            var dnsServers = properties.DnsAddresses
                .Where(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(address => address.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            result.Add(new AdapterSnapshot
            {
                InterfaceId = networkInterface.Id,
                InterfaceIndex = ipv4.Index,
                DhcpDns = UsesDhcpDns(networkInterface.Id),
                DnsServers = dnsServers,
            });
        }

        return result;
    }

    private static bool IsCandidate(NetworkInterface networkInterface)
    {
        if (networkInterface.OperationalStatus != OperationalStatus.Up ||
            networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
        {
            return false;
        }

        if (networkInterface.Description.Contains("Wintun", StringComparison.OrdinalIgnoreCase) ||
            networkInterface.Name.Contains("Dirac", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var properties = networkInterface.GetIPProperties();
            var hasIpv4 = properties.UnicastAddresses.Any(item =>
                item.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(item.Address));
            var hasIpv4Gateway = properties.GatewayAddresses.Any(item =>
                item.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                !item.Address.Equals(IPAddress.Any));

            return hasIpv4 && hasIpv4Gateway;
        }
        catch (NetworkInformationException)
        {
            return false;
        }
    }


    private static bool UsesDhcpDns(string interfaceId)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var id = interfaceId.Trim().Trim('{', '}');
        using var key = Registry.LocalMachine.OpenSubKey(
            $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{{{id}}}",
            writable: false);
        var staticNameServer = key?.GetValue("NameServer") as string;
        return string.IsNullOrWhiteSpace(staticNameServer);
    }

    private static NetworkInterface? FindInterface(string interfaceId)
    {
        var normalized = interfaceId.Trim().Trim('{', '}');
        return NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(item => item.Id.Trim().Trim('{', '}').Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task SetServerAddressesAsync(int interfaceIndex, IReadOnlyList<string> servers)
    {
        if (servers.Count == 0)
        {
            throw new ArgumentException("At least one DNS server is required.", nameof(servers));
        }

        foreach (var server in servers)
        {
            if (!IPAddress.TryParse(server, out var address) ||
                address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                throw new InvalidOperationException($"Invalid IPv4 DNS server in snapshot: {server}");
            }
        }

        var arrayExpression = string.Join(",", servers.Select(item => $"'{item}'"));
        var command =
            $"Set-DnsClientServerAddress -InterfaceIndex {interfaceIndex} -ServerAddresses @({arrayExpression}) -Validate:$false -ErrorAction Stop";

        await RunPowerShellAsync(command);
    }

    private static Task ResetServerAddressesAsync(int interfaceIndex)
    {
        return RunPowerShellAsync(
            $"Set-DnsClientServerAddress -InterfaceIndex {interfaceIndex} -ResetServerAddresses -ErrorAction Stop");
    }

    private static Task FlushDnsCacheAsync()
    {
        return RunPowerShellAsync("Clear-DnsClientCache -ErrorAction Stop");
    }

    private static async Task RunPowerShellAsync(string command)
    {
        var result = await Cli.Wrap("powershell.exe")
            .WithArguments(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command])
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        if (result.ExitCode != 0)
        {
            var error = result.StandardError.Trim();
            if (error.IsNullOrEmpty())
            {
                error = result.StandardOutput.Trim();
            }

            throw new InvalidOperationException($"Windows DNS command failed with exit code {result.ExitCode}: {error}");
        }
    }
}