[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+([\-+][0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [ValidateSet('stable','beta')]
    [string]$Channel = 'stable',

    [Parameter(Mandatory)]
    [string]$RuntimeBinSource,

    [Parameter(Mandatory)]
    [string]$VpkExe,

    [string]$DotnetExe = 'dotnet',

    [string]$OutputDir = ''
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'v2rayN\v2rayN.Desktop\v2rayN.Desktop.csproj'
$amazTool = Join-Path $repo 'v2rayN\AmazTool\AmazTool.csproj'

$expectedXray = '5B4DBCF2F8E2E1D9A7E1BB47C5EE06FC355E30F70DDCD0CEE1D37A662CFFE01D'
$expectedWintun = 'E5DA8447DC2C320EDC0FC52FA01885C103DE8C118481F683643CACC3220DAFCE'

$xray = Join-Path $RuntimeBinSource 'xray.exe'
$wintun = Join-Path $RuntimeBinSource 'wintun.dll'
$marker = Join-Path $RuntimeBinSource 'dirac-core.sha256'

foreach($required in @($project,$amazTool,$VpkExe,$xray,$wintun,$marker)){
    if(-not (Test-Path -LiteralPath $required)){
        throw "Required release input missing: $required"
    }
}

if((Get-FileHash -LiteralPath $xray -Algorithm SHA256).Hash -ne $expectedXray){
    throw 'Pinned Dirac Xray hash mismatch.'
}
if((Get-FileHash -LiteralPath $wintun -Algorithm SHA256).Hash -ne $expectedWintun){
    throw 'Pinned Wintun hash mismatch.'
}
if(([IO.File]::ReadAllText($marker).Trim()).ToUpperInvariant() -ne $expectedXray){
    throw 'dirac-core.sha256 does not match the pinned Xray.'
}

if([string]::IsNullOrWhiteSpace($OutputDir)){
    $OutputDir = Join-Path $repo "artifacts\velopack\$Version-$Channel"
}
$work = Join-Path $repo "artifacts\publish\$Version-$Channel"
if(Test-Path $work){Remove-Item -LiteralPath $work -Recurse -Force}
New-Item -ItemType Directory -Path $work,$OutputDir -Force|Out-Null

& $DotnetExe publish $project -c Release -r win-x64 -p:SelfContained=true "-p:Version=$Version" -o $work
if($LASTEXITCODE -ne 0){throw 'Dirac publish failed.'}

& $DotnetExe publish $amazTool -c Release -r win-x64 -p:SelfContained=true -p:PublishTrimmed=true "-p:Version=$Version" -o $work
if($LASTEXITCODE -ne 0){throw 'AmazTool publish failed.'}

$runtimeDest = Join-Path $work 'bin\xray'
New-Item -ItemType Directory -Path $runtimeDest -Force|Out-Null
Copy-Item -LiteralPath $xray -Destination (Join-Path $runtimeDest 'xray.exe') -Force
Copy-Item -LiteralPath $wintun -Destination (Join-Path $runtimeDest 'wintun.dll') -Force
Copy-Item -LiteralPath $marker -Destination (Join-Path $runtimeDest 'dirac-core.sha256') -Force
foreach($optional in @('geoip.dat','geosite.dat')){
    $src=Join-Path $RuntimeBinSource $optional
    if(Test-Path -LiteralPath $src){
        Copy-Item -LiteralPath $src -Destination (Join-Path $runtimeDest $optional) -Force
    }
}

if(Test-Path (Join-Path $work 'guiConfigs')){
    throw 'Mutable user configuration unexpectedly entered release payload.'
}

$releaseChannel = "win-x64-$Channel"
& $VpkExe pack --packId 'Dimsho.Dirac.Desktop' --packTitle 'Dirac' --packVersion $Version --packDir $work --mainExe 'v2rayN.exe' --runtime 'win-x64' --channel $releaseChannel --outputDir $OutputDir
if($LASTEXITCODE -ne 0){throw 'Velopack packaging failed.'}

$feed = Join-Path $OutputDir "releases.$releaseChannel.json"
$setup = @(Get-ChildItem -LiteralPath $OutputDir -File -Filter '*Setup*.exe' -ErrorAction SilentlyContinue)
$full = @(Get-ChildItem -LiteralPath $OutputDir -File -Filter '*-full.nupkg' -ErrorAction SilentlyContinue)
if(-not(Test-Path $feed) -or $setup.Count -lt 1 -or $full.Count -lt 1){
    throw 'Velopack output is incomplete.'
}

Write-Output "DIRAC_VELOPACK_PACKAGE_OK=true"
Write-Output "VERSION=$Version"
Write-Output "CHANNEL=$releaseChannel"
Write-Output "OUTPUT=$OutputDir"
Write-Output "XRAY_SHA256=$expectedXray"
Write-Output "WINTUN_SHA256=$expectedWintun"