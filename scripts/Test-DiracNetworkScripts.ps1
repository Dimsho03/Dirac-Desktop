# Offline checks only; no adapters, processes or DNS settings are modified.
$ErrorActionPreference = 'Stop'
$probe = Join-Path $PSScriptRoot 'Test-DiracNetwork.ps1'
$rollback = Join-Path $PSScriptRoot 'Restore-DiracNetwork.ps1'
$failures = [Collections.Generic.List[string]]::new()
foreach ($file in @($probe,$rollback)) {
    if (-not (Test-Path -LiteralPath $file)) { throw "Required script missing: $file" }
    $tokens = $null; $parseErrors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($file,[ref]$tokens,[ref]$parseErrors)
    if (@($parseErrors).Count -gt 0) {
        $failures.Add(('PARSE_ERROR ' + $file + ': ' + ($parseErrors | Out-String)))
    }
}
if ($failures.Count -gt 0) { throw ($failures -join [Environment]::NewLine) }
$dir = Join-Path ([IO.Path]::GetTempPath()) ('dirac-script-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dir -Force | Out-Null
function Fixture([string]$mode,[string[]]$dns,[bool]$xray,[bool]$tun,[bool]$route,[bool]$port,[bool]$tcp,[bool]$resolve,[bool]$https,[object]$baseline) {
    return [pscustomobject]@{
        PhysicalUp = $true; PhysicalAdapters = @([pscustomobject]@{
            Name='Wi-Fi';InterfaceId='TEST-GUID';InterfaceIndex=12;DnsServers=@($dns)
        })
        XrayRunning=$xray;TunUp=$tun;TunDefaultRoute=$route;XrayOwnsDns53=$port
        Tcp443=$tcp;DnsResolved=$resolve;HttpsSucceeded=$https;BaselineAdapters=$baseline
    }
}
function Assert([string]$name,[object]$fixture,[string]$mode,[bool]$healthy,[string]$code) {
    $path = Join-Path $dir ($name+'.json')
    $fixture | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path -Encoding UTF8
    $json = & $probe -Mode $mode -FixtureFile $path | Out-String
    $result = $json | ConvertFrom-Json -ErrorAction Stop
    if ($result.Healthy -ne $healthy) { $failures.Add($name+': wrong Healthy state') }
    if ($code -and (@($result.Failures) -notcontains $code)) { $failures.Add($name+': missing '+$code) }
    Write-Output ('TEST_'+$name+'='+$(if($result.Healthy -eq $healthy -and (!$code -or @($result.Failures) -contains $code)){'PASS'}else{'FAIL'}))
}
try {
    # Script exits 2 on failed observation: avoid treating that as a test framework error.
    $baseline=@([pscustomobject]@{ Name='Wi-Fi';InterfaceId='TEST-GUID';InterfaceIndex=12;DnsServers=@('192.168.0.1');DhcpDns=$true })
    Assert 'connected_loopback_ok' (Fixture Connected @('127.0.0.1') $true $true $true $true $true $true $true $null) Connected $true ''
    Assert 'connected_wrong_dns' (Fixture Connected @('192.168.0.1') $true $true $true $true $true $true $true $null) Connected $false 'CONNECTED_PHYSICAL_DNS_NOT_LOOPBACK:Wi-Fi'
    Assert 'connected_no_https' (Fixture Connected @('127.0.0.1') $true $true $true $true $true $true $false $null) Connected $false 'HTTPS_FAILED'
    Assert 'connected_wrong_owner' (Fixture Connected @('127.0.0.1') $true $true $true $false $true $true $true $null) Connected $false 'EXPECTED_XRAY_NOT_OWNING_UDP53'
    Assert 'ordinary_baseline_ok' (Fixture Ordinary @('192.168.0.1') $false $false $false $false $true $true $true $baseline) Ordinary $true ''
    Assert 'ordinary_dns_leak' (Fixture Ordinary @('127.0.0.1') $false $false $false $false $true $true $true $baseline) Ordinary $false 'ORDINARY_PHYSICAL_DNS_STILL_LOOPBACK:Wi-Fi'
    Assert 'ordinary_stale_tun' (Fixture Ordinary @('192.168.0.1') $false $true $true $false $true $true $true $baseline) Ordinary $false 'ORPHANED_TUN_OR_ROUTE'
    Assert 'ordinary_stale_dns' (Fixture Ordinary @('1.1.1.1') $false $false $false $false $true $true $true $baseline) Ordinary $false 'BASELINE_DNS_NOT_RESTORED:Wi-Fi'
    # The rollback -WhatIf path must validate the saved snapshot while causing
    # NO DNS, adapter, or process changes. Dummy executables are never run.
    $fakeRoot = Join-Path $dir 'dummy-stage'
    $fakeBin = Join-Path $fakeRoot 'bin\xray'
    New-Item -ItemType Directory -Path $fakeBin -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $fakeRoot 'v2rayN.exe') -Value 'dummy' -Encoding ASCII
    Set-Content -LiteralPath (Join-Path $fakeBin 'xray.exe') -Value 'dummy' -Encoding ASCII
    $fakeBaseline = [pscustomobject]@{
        Schema = 1
        Adapters = @([pscustomobject]@{
            Name='Wi-Fi'; InterfaceId='00000000-0000-0000-0000-000000000001'
            InterfaceIndex=12;DhcpDns=$true;DnsServers=@('192.168.0.1')
        })
    }
    $fakeBaselineFile = Join-Path $dir 'baseline.json'
    $fakeBaseline | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $fakeBaselineFile -Encoding UTF8
    $fakeResult = Join-Path $dir 'should-not-be-created'
    $plan = (& $rollback -GuardedRoot $fakeRoot -BaselineFile $fakeBaselineFile -ResultFolder $fakeResult -WhatIf | Out-String)
    if ($plan -notmatch 'ROLLBACK_DRY_RUN=true' -or (Test-Path $fakeResult)) {
        $failures.Add('rollback_whatif_must_not_mutate_network_or_create_output')
        Write-Output 'TEST_rollback_whatif=FAIL'
    } else { Write-Output 'TEST_rollback_whatif=PASS' }
    if ($failures.Count -gt 0) { throw ($failures -join [Environment]::NewLine) }
    Write-Output 'DIRAC_OFFLINE_NETWORK_CHECK_TESTS_PASS=9'
} finally { Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue }
