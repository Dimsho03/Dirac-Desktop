# Registers or safely disarms an INDEPENDENT one-shot SYSTEM rollback task.
# This file never starts a VPN and does not modify DNS or adapters.
[CmdletBinding(SupportsShouldProcess=$true,ConfirmImpact='High')]
param(
    [Parameter(Mandatory)][string]$GuardedRoot,
    [Parameter(Mandatory)][string]$BaselineFile,
    [Parameter(Mandatory)][string]$ResultFolder,
    [Parameter(Mandatory)][ValidatePattern('^DiracRollback-[a-zA-Z0-9_-]{6,50}$')][string]$TaskName,
    [ValidateRange(5,15)][int]$DelayMinutes=7,
    [switch]$Disarm,
    [string]$TunName='dirac_single_tun'
)
$ErrorActionPreference='Stop'
$GuardedRoot=[IO.Path]::GetFullPath($GuardedRoot)
$BaselineFile=[IO.Path]::GetFullPath($BaselineFile)
$ResultFolder=[IO.Path]::GetFullPath($ResultFolder)
$restore=Join-Path $PSScriptRoot 'Restore-DiracNetwork.ps1'
$probe=Join-Path $PSScriptRoot 'Test-DiracNetwork.ps1'
$gui=Join-Path $GuardedRoot 'v2rayN.exe'
$xray=Join-Path $GuardedRoot 'bin\xray\xray.exe'
foreach($f in @($restore,$probe,$gui,$xray,$BaselineFile)){
    if(!(Test-Path -LiteralPath $f)){throw ('Required recovery component missing: '+$f)}
    if($f -match '["\r\n]'){throw 'Unquotable path in recovery configuration.'}
}
if($ResultFolder -match '["\r\n]'){throw 'Unquotable result directory.'}
$baseline=Get-Content -LiteralPath $BaselineFile -Encoding UTF8 -Raw|ConvertFrom-Json -ErrorAction Stop
if($baseline.Schema -ne 1 -or @($baseline.Adapters).Count -lt 1){throw 'Invalid baseline; no watchdog registered.'}
if($WhatIfPreference){
    Write-Output ('WATCHDOG_DRY_RUN=true ACTION='+$(if($Disarm){'DISARM'}else{'REGISTER'})+
        ' TASK='+$TaskName+' DELAY_MIN='+$DelayMinutes)
    return
}
$principal=[Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
    throw 'Registering the recovery SYSTEM task requires elevation.'
}
$existing=Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if($Disarm){
    if(!$existing){Write-Output 'WATCHDOG_ALREADY_ABSENT=true';return}
    $connected=Test-Path (Join-Path $ResultFolder 'success.ok')
    $restored=Test-Path (Join-Path $ResultFolder 'rollback-completed.ok')
    if(-not $connected -and -not $restored){throw 'Refusing to disarm without a verified result marker.'}
    $mode=if($connected){'Connected'}else{'Ordinary'}
    $check=(& $probe -Mode $mode -ExpectedXrayExe $xray -TunName $TunName -BaselineFile $BaselineFile|Out-String).Trim()|
        ConvertFrom-Json -ErrorAction Stop
    if(-not $check.Healthy){throw ('Refusing to disarm: '+(@($check.Failures) -join ','))}
    if($PSCmdlet.ShouldProcess($TaskName,'Unregister verified recovery task')){
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction Stop
        Write-Output ('WATCHDOG_DISARMED_AFTER_VERIFIED_'+$mode.ToUpperInvariant()+'=true')
    }
    return
}
if($existing){throw 'The specified watchdog task already exists; refusing replacement.'}
if(Test-Path -LiteralPath (Join-Path $ResultFolder 'success.ok')){throw 'Success marker exists; use a fresh switch directory.'}
if(Test-Path -LiteralPath (Join-Path $ResultFolder 'rollback-completed.ok')){throw 'Rollback marker exists; use a fresh switch directory.'}
New-Item -ItemType Directory -Path $ResultFolder -Force|Out-Null
$argument='-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+$restore+'" -GuardedRoot "'+
    $GuardedRoot+'" -BaselineFile "'+$BaselineFile+'" -ResultFolder "'+$ResultFolder+'" -Confirm:$false'
$exe=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
if(!(Test-Path $exe)){throw 'Windows PowerShell required for independent rollback task.'}
$action=New-ScheduledTaskAction -Execute $exe -Argument $argument
$trigger=New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes($DelayMinutes)
if($PSCmdlet.ShouldProcess($TaskName,'Register one-shot recovery task as SYSTEM')){
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -User SYSTEM -RunLevel Highest -ErrorAction Stop | Out-Null
    $check=Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop
    if(!$check){throw 'Scheduled task verification failed.'}
    Write-Output ('WATCHDOG_REGISTERED=true TASK='+$TaskName+' DELAY_MIN='+$DelayMinutes)
}
