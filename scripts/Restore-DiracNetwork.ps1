# Scoped emergency rollback for one staged Dirac build. Never kills unrelated VPNs.
[CmdletBinding(SupportsShouldProcess=$true,ConfirmImpact='High')]
param(
    [Parameter(Mandatory)][string]$GuardedRoot,
    [Parameter(Mandatory)][string]$BaselineFile,
    [Parameter(Mandatory)][string]$ResultFolder,
    [switch]$RestartPreviousVpn,
    [string]$PreviousXrayExe = '',
    [string]$PreviousConfigFile = '',
    [string]$TunName = 'dirac_single_tun'
)
$ErrorActionPreference='Stop'
$GuardedRoot=[IO.Path]::GetFullPath($GuardedRoot)
$BaselineFile=[IO.Path]::GetFullPath($BaselineFile)
$ResultFolder=[IO.Path]::GetFullPath($ResultFolder)
$gui=Join-Path $GuardedRoot 'v2rayN.exe'
$xray=Join-Path $GuardedRoot 'bin\xray\xray.exe'
$stateFile=Join-Path $GuardedRoot 'binConfigs\dirac-dns-guard-state.json'
$probe=Join-Path $PSScriptRoot 'Test-DiracNetwork.ps1'
$success=Join-Path $ResultFolder 'success.ok'
$done=Join-Path $ResultFolder 'rollback-completed.ok'
function SameDns([string[]]$a,[string[]]$b) {
    return [string]::Join('|',@($a|Sort-Object -Unique)) -ceq [string]::Join('|',@($b|Sort-Object -Unique))
}
function ExactProcess([string]$file,[string]$name) {
    return @(Get-CimInstance Win32_Process -Filter ("Name='"+$name+"'") -ErrorAction Stop |
        Where-Object { $_.ExecutablePath -and [string]::Equals($_.ExecutablePath,$file,[StringComparison]::OrdinalIgnoreCase) })
}
if(!(Test-Path $BaselineFile)){throw 'Missing saved DNS baseline; refusing to guess DHCP or static configuration.'}
if(!(Test-Path $gui) -or !(Test-Path $xray) -or !(Test-Path $probe)){throw 'Incomplete staged Dirac or diagnostic scripts.'}
$baseline=Get-Content -LiteralPath $BaselineFile -Raw -Encoding UTF8|ConvertFrom-Json -ErrorAction Stop
if($baseline.Schema -ne 1 -or @($baseline.Adapters).Count -lt 1){throw 'Invalid or empty baseline.'}
$seen=@{}
foreach($adapter in @($baseline.Adapters)){
    $id=[Guid]::Empty
    if(-not [Guid]::TryParse([string]$adapter.InterfaceId,[ref]$id)){throw 'Invalid baseline adapter GUID.'}
    if($seen.ContainsKey($id.ToString())){throw 'Duplicate baseline adapter.'}
    $seen[$id.ToString()]=$true
    if(-not $adapter.DhcpDns -and @($adapter.DnsServers).Count -lt 1){throw 'Static DNS baseline is empty.'}
    foreach($server in @($adapter.DnsServers)){
        $ip=[Net.IPAddress]::None
        if(-not [Net.IPAddress]::TryParse([string]$server,[ref]$ip) -or
            $ip.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork){throw 'Invalid baseline DNS address.'}
    }
}
if($RestartPreviousVpn -and (!(Test-Path $PreviousXrayExe) -or !(Test-Path $PreviousConfigFile))){throw 'Previous VPN paths missing.'}
if(Test-Path $success){Write-Output 'ROLLBACK_SKIPPED_NEW_VPN_VERIFIED=true';return}
if(Test-Path $done){Write-Output 'ROLLBACK_ALREADY_VERIFIED=true';return}
if($WhatIfPreference){
    Write-Output ('ROLLBACK_DRY_RUN=true STAGED_ROOT='+$GuardedRoot+' ADAPTERS='+@($baseline.Adapters).Count)
    return
}
$identity=[Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if(-not $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Administrator rights required.'}
New-Item -ItemType Directory -Path $ResultFolder -Force|Out-Null
$log=Join-Path $ResultFolder 'rollback.log'
function Record([string]$message){
    Add-Content -LiteralPath $log -Encoding UTF8 -Value ([DateTime]::UtcNow.ToString('o')+' '+$message)
    Write-Output $message
}
Record 'ROLLBACK_BEGIN'
foreach($entry in @(@{Exe=$gui;Name='v2rayN.exe'},@{Exe=$xray;Name='xray.exe'})){
    foreach($proc in @(ExactProcess $entry.Exe $entry.Name)){
        if($PSCmdlet.ShouldProcess($entry.Exe,'Stop staged PID '+$proc.ProcessId)){
            try {
                Stop-Process -Id $proc.ProcessId -Force -ErrorAction Stop
                Record ('SCOPED_PROCESS_STOPPED='+$entry.Name+' PID='+$proc.ProcessId)
            } catch {
                # A GUI shutdown can reap its Xray child before Stop-Process
                # executes. Only tolerate a race when this exact PID and
                # executable are gone; never suppress an access failure.
                $remaining = @(ExactProcess $entry.Exe $entry.Name |
                    Where-Object { $_.ProcessId -eq $proc.ProcessId })
                if ($remaining.Count -gt 0) { throw }
                Record ('SCOPED_PROCESS_ALREADY_EXITED='+$entry.Name+' PID='+$proc.ProcessId)
            }
        }
    }
}
Start-Sleep -Seconds 3
if(@(ExactProcess $xray 'xray.exe').Count -gt 0){throw 'Staged Xray did not stop; refusing DNS changes.'}
$restore=@($baseline.Adapters)
if(Test-Path -LiteralPath $stateFile){
    try{
        $guard=Get-Content -LiteralPath $stateFile -Raw -Encoding UTF8|ConvertFrom-Json -ErrorAction Stop
        if(@($guard.Adapters).Count -gt 0){
            $compatible=$true
            foreach($item in @($guard.Adapters)){
                $found=@($baseline.Adapters|Where-Object {$_.InterfaceId -eq $item.InterfaceId})
                if($found.Count -ne 1 -or $found[0].DhcpDns -ne $item.DhcpDns -or
                    -not (SameDns @($found[0].DnsServers) @($item.DnsServers))){$compatible=$false}
            }
            if($compatible){$restore=@($guard.Adapters);Record 'MATCHING_GUARD_SNAPSHOT=true'}
            else{Record 'STALE_GUARD_SNAPSHOT_IGNORED=true'}
        }
    }catch{Record 'GUARD_SNAPSHOT_UNREADABLE_USING_BASELINE=true'}
}
$failures=[Collections.Generic.List[string]]::new()
foreach($snapshot in $restore){
    $guid=([Guid]::Parse([string]$snapshot.InterfaceId)).ToString('D')
    $found=@(Get-NetAdapter -IncludeHidden -ErrorAction Stop|Where-Object {
        $_.InterfaceGuid -and ([Guid]::Parse($_.InterfaceGuid.ToString())).ToString('D') -eq $guid
    })
    if($found.Count -ne 1){$failures.Add('ADAPTER_NOT_FOUND:'+ $snapshot.Name);continue}
    try{
        if($snapshot.DhcpDns){
            Set-DnsClientServerAddress -InterfaceIndex $found[0].ifIndex -ResetServerAddresses -ErrorAction Stop
        }else{
            Set-DnsClientServerAddress -InterfaceIndex $found[0].ifIndex -ServerAddresses @($snapshot.DnsServers) -ErrorAction Stop
        }
        Record ('DNS_RESTORED='+$snapshot.Name+' DHCP='+[bool]$snapshot.DhcpDns)
    }catch{$failures.Add('DNS_RESTORE_FAILED:'+ $snapshot.Name)}
}
Clear-DnsClientCache -ErrorAction SilentlyContinue
if($failures.Count -gt 0){throw ('DNS rollback incomplete: '+($failures -join ','))}
$foreignXray=@(Get-CimInstance Win32_Process -Filter "Name='xray.exe'" -ErrorAction Stop)
$orphan=@(Get-NetAdapter -Name $TunName -ErrorAction SilentlyContinue|Where-Object {$_.Status -eq 'Up'})
if($orphan.Count -gt 0 -and $foreignXray.Count -eq 0){
    Disable-NetAdapter -Name $TunName -Confirm:$false -ErrorAction Stop
    Record ('DISABLED_ORPHANED_TUN='+$TunName)
}
$json=(& $probe -Mode Ordinary -ExpectedXrayExe $xray -TunName $TunName -BaselineFile $BaselineFile|Out-String).Trim()
$checked=$json|ConvertFrom-Json -ErrorAction Stop
if(-not $checked.Healthy){
    Record ('NETWORK_STILL_UNHEALTHY='+(@($checked.Failures) -join ','))
    throw 'Ordinary HTTPS/DNS/TUN validation failed; keep recovery watchdog armed.'
}
Record 'ORDINARY_NETWORK_VERIFIED=true'
if(Test-Path $stateFile){Remove-Item -LiteralPath $stateFile -Force -ErrorAction Stop}
Set-Content -LiteralPath $done -Encoding ASCII -Value 'ordinary network verified'
Record 'ROLLBACK_ORDINARY_COMPLETED=true'
if($RestartPreviousVpn){
    if(@(ExactProcess $PreviousXrayExe 'xray.exe').Count -eq 0){
        if($PSCmdlet.ShouldProcess($PreviousXrayExe,'Restart previous VPN after ordinary internet verification')){
            Start-Process -FilePath $PreviousXrayExe -ArgumentList ('run -c "'+$PreviousConfigFile+'"') -WorkingDirectory (Split-Path $PreviousXrayExe -Parent) -ErrorAction Stop|Out-Null
            Record 'PREVIOUS_VPN_RESTART_REQUESTED=true'
        }
    }else{Record 'PREVIOUS_VPN_ALREADY_RUNNING=true'}
}
