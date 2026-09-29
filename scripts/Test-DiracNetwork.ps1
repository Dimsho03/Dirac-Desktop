# Requires Windows PowerShell 5.1+; performs NO network changes.
[CmdletBinding()]
param(
    [ValidateSet('Ordinary','Connected')]
    [string]$Mode = 'Ordinary',
    [string]$ExpectedXrayExe = '',
    [string]$TunName = 'dirac_single_tun',
    [string]$BaselineFile = '',
    [string]$SaveBaselineFile = '',
    [switch]$CaptureBaselineOnly,
    [string]$FixtureFile = '',
    [string[]]$HttpsUrls = @('https://example.com','https://www.cloudflare.com/cdn-cgi/trace')
)
$ErrorActionPreference = 'Stop'
function Same-Addresses([string[]]$a,[string[]]$b) {
    $left = @( $a | Where-Object { $_ } | Sort-Object -Unique )
    $right = @( $b | Where-Object { $_ } | Sort-Object -Unique )
    return [string]::Join('|',$left) -ceq [string]::Join('|',$right)
}
function Assess([object]$o,[string]$kind) {
    $fail = [Collections.Generic.List[string]]::new()
    if (-not $o.PhysicalUp) { $fail.Add('PHYSICAL_INTERFACE_DOWN') }
    if (@($o.PhysicalAdapters).Count -lt 1) { $fail.Add('NO_PHYSICAL_DEFAULT_ROUTE') }
    if ($kind -eq 'Connected') {
        if (-not $o.XrayRunning) { $fail.Add('EXPECTED_XRAY_NOT_RUNNING') }
        if (-not $o.XrayOwnsDns53) { $fail.Add('EXPECTED_XRAY_NOT_OWNING_UDP53') }
        if (-not $o.TunUp) { $fail.Add('NATIVE_TUN_NOT_UP') }
        if (-not $o.TunDefaultRoute) { $fail.Add('TUN_DEFAULT_ROUTE_MISSING') }
        foreach ($physical in @($o.PhysicalAdapters)) {
            if (-not (Same-Addresses @($physical.DnsServers) @('127.0.0.1'))) {
                $fail.Add('CONNECTED_PHYSICAL_DNS_NOT_LOOPBACK:' + $physical.Name)
            }
        }
    }
    else {
        if ($o.XrayRunning) { $fail.Add('GUARDED_XRAY_STILL_RUNNING') }
        if ($o.TunUp -or $o.TunDefaultRoute) { $fail.Add('ORPHANED_TUN_OR_ROUTE') }
        foreach ($physical in @($o.PhysicalAdapters)) {
            if (@($physical.DnsServers) -contains '127.0.0.1') {
                $fail.Add('ORDINARY_PHYSICAL_DNS_STILL_LOOPBACK:' + $physical.Name)
            }
        }
    }
    if ($null -ne $o.BaselineAdapters -and $kind -eq 'Ordinary') {
        foreach ($original in @($o.BaselineAdapters)) {
            $matches = @($o.PhysicalAdapters | Where-Object { $_.InterfaceId -eq $original.InterfaceId })
            if ($matches.Count -ne 1 -or -not (Same-Addresses @($matches[0].DnsServers) @($original.DnsServers))) {
                $fail.Add('BASELINE_DNS_NOT_RESTORED:' + $original.Name)
            }
        }
    }
    if (-not $o.Tcp443) { $fail.Add('TCP443_FAILED') }
    if (-not $o.DnsResolved) { $fail.Add('DNS_LOOKUP_FAILED') }
    if (-not $o.HttpsSucceeded) { $fail.Add('HTTPS_FAILED') }
    return [pscustomobject]@{
        Schema = 1
        Mode = $kind
        Healthy = ($fail.Count -eq 0)
        Failures = @($fail.ToArray())
        Observation = $o
    }
}
function ProbeTcp {
    $client = [Net.Sockets.TcpClient]::new()
    try {
        $attempt = $client.ConnectAsync('1.1.1.1',443)
        return ($attempt.Wait([TimeSpan]::FromSeconds(6)) -and $client.Connected)
    } catch { return $false } finally { $client.Dispose() }
}
function ProbeHttps([string]$url) {
    # Avoid system proxy or browser cache: the TUN route itself must pass HTTPS.
    $curl = Join-Path $env:WINDIR 'System32\curl.exe'
    if (-not (Test-Path -LiteralPath $curl)) { return $false }
    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $curl
    $psi.Arguments = '--noproxy "*" --silent --show-error --max-time 11 --output NUL --write-out "%{http_code}" "' + $url + '"'
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $proc = [Diagnostics.Process]::new()
    $proc.StartInfo = $psi
    try {
        [void]$proc.Start()
        if (-not $proc.WaitForExit(14000)) { $proc.Kill(); return $false }
        $stdout = $proc.StandardOutput.ReadToEnd().Trim()
        return ($proc.ExitCode -eq 0 -and $stdout -match '^2\d\d$|^3\d\d$')
    } catch { return $false } finally { $proc.Dispose() }
}
function CaptureObservation {
    $defaults = @(Get-NetRoute -AddressFamily IPv4 -DestinationPrefix '0.0.0.0/0' -ErrorAction Stop)
    $all = @(Get-NetAdapter -ErrorAction Stop)
    # Capture the adapter before filtering its default routes: nested $_ would
    # otherwise refer to the route instead of the network adapter.
    $physical = @($all | Where-Object { $_.Status -eq 'Up' -and $_.Name -notmatch '(?i)(tun|dirac|xray)' } |
        ForEach-Object {
            $adapter = $_
            if (@($defaults | Where-Object { $_.InterfaceIndex -eq $adapter.ifIndex }).Count -gt 0) { $adapter }
        })
    $adapters = @(
        foreach ($a in $physical) {
            $dns = Get-DnsClientServerAddress -InterfaceIndex $a.ifIndex -AddressFamily IPv4 -ErrorAction Stop
            $guid = if ($a.InterfaceGuid) { $a.InterfaceGuid.ToString() } else { $a.InterfaceDescription }
            [pscustomobject]@{
                Name = $a.Name
                InterfaceId = $guid
                InterfaceIndex = [int]$a.ifIndex
                DnsServers = @($dns.ServerAddresses)
            }
        }
    )
    $tun = $all | Where-Object { $_.Name -ieq $TunName } | Select-Object -First 1
    $tunRoutes = @($defaults | Where-Object { $tun -and $_.InterfaceIndex -eq $tun.ifIndex })
    $candidate = @()
    if ($ExpectedXrayExe) {
        $fullPath = [IO.Path]::GetFullPath($ExpectedXrayExe)
        $candidate = @(Get-CimInstance Win32_Process -Filter "Name='xray.exe'" -ErrorAction Stop |
            Where-Object { $_.ExecutablePath -and [string]::Equals($_.ExecutablePath,$fullPath,[StringComparison]::OrdinalIgnoreCase) })
    }
    $pid = if ($candidate.Count -eq 1) { [int]$candidate[0].ProcessId } else { -1 }
    $owns53 = $pid -gt 0 -and @(
        Get-NetUDPEndpoint -LocalPort 53 -ErrorAction SilentlyContinue |
            Where-Object { $_.OwningProcess -eq $pid }
    ).Count -gt 0
    $dnsWorks = $false
    try { $dnsWorks = @(Resolve-DnsName example.com -Type A -QuickTimeout -ErrorAction Stop |
        Where-Object { $_.IPAddress }).Count -gt 0 } catch {}
    $httpsWorks = $false
    foreach ($url in $HttpsUrls) {
        if ($url -notmatch '^https://[a-zA-Z0-9.-]+(/[a-zA-Z0-9/_\.-]*)?$') {
            throw 'HTTPS URL must be a plain HTTPS hostname/path, without query parameters.'
        }
        if (ProbeHttps $url) { $httpsWorks = $true; break }
    }
    return [pscustomobject]@{
        PhysicalUp = @($adapters).Count -gt 0
        PhysicalAdapters = @($adapters)
        XrayRunning = $candidate.Count -eq 1
        XrayOwnsDns53 = [bool]$owns53
        TunUp = [bool]($tun -and $tun.Status -eq 'Up')
        TunDefaultRoute = $tunRoutes.Count -gt 0
        Tcp443 = [bool](ProbeTcp)
        DnsResolved = [bool]$dnsWorks
        HttpsSucceeded = [bool]$httpsWorks
        BaselineAdapters = $null
    }
}
if ($FixtureFile) {
    if ($SaveBaselineFile -or $CaptureBaselineOnly) { throw 'Fixtures cannot write baseline snapshots.' }
    $observation = Get-Content -LiteralPath $FixtureFile -Raw -Encoding UTF8 | ConvertFrom-Json -ErrorAction Stop
}
else {
    $observation = CaptureObservation
}
if ($SaveBaselineFile -or $CaptureBaselineOnly) {
    if (-not $SaveBaselineFile -or $Mode -ne 'Ordinary') {
        throw 'Baseline capture requires -Mode Ordinary -SaveBaselineFile.'
    }
    if (Test-Path -LiteralPath $SaveBaselineFile) { throw 'Refusing to overwrite an existing baseline.' }
    if (@($observation.PhysicalAdapters).Count -lt 1) { throw 'No physical default-route adapter to preserve.' }
    $snapshots = @(
        foreach ($a in @($observation.PhysicalAdapters)) {
            $guid = $a.InterfaceId.Trim().Trim('{','}')
            $key = Get-ItemProperty -LiteralPath ('HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{' + $guid + '}') -ErrorAction SilentlyContinue
            [pscustomobject]@{
                Name = $a.Name; InterfaceId = $a.InterfaceId; InterfaceIndex = $a.InterfaceIndex
                DhcpDns = -not [bool]($key.NameServer)
                DnsServers = @($a.DnsServers)
            }
        }
    )
    if ($snapshots.Count -lt 1) { throw 'Baseline snapshot is empty.' }
    $snapshot = [pscustomobject]@{ Schema = 1; CapturedUtc = [DateTime]::UtcNow.ToString('o'); Adapters = @($snapshots) }
    $snapshot | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $SaveBaselineFile -Encoding UTF8 -ErrorAction Stop
    if ($CaptureBaselineOnly) {
        [pscustomobject]@{ BaselineCaptured = $true; File = $SaveBaselineFile; AdapterCount = $snapshots.Count } | ConvertTo-Json -Compress
        return
    }
}
if ($BaselineFile) {
    $snapshot = Get-Content -LiteralPath $BaselineFile -Raw -Encoding UTF8 | ConvertFrom-Json -ErrorAction Stop
    if ($snapshot.Schema -ne 1 -or @($snapshot.Adapters).Count -lt 1) { throw 'Invalid baseline schema.' }
    $observation.BaselineAdapters = @($snapshot.Adapters)
}
$result = Assess $observation $Mode
$result | ConvertTo-Json -Depth 9 -Compress
if (-not $result.Healthy) { exit 2 }
