[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+([\-+][0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [ValidateSet('stable','beta')]
    [string]$Channel = 'stable',

    [Parameter(Mandatory)]
    [string]$RuntimeBinSource,

    [string]$GeoDataSource = '',

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
$expectedGeoip = '3FF5C8723894A880B4AF93E1B0436C39226A98FBB64B6CD42C184588087241A7'
$expectedGeosite = '9B03F2E7B978D524E437D49869569B74124B0D744A3721CB38CF7522188FBFE4'
if([string]::IsNullOrWhiteSpace($GeoDataSource)){
    $GeoDataSource = Join-Path $repo 'resources\ru-routing'
}$geoip = Join-Path $GeoDataSource 'geoip.dat'
$geosite = Join-Path $GeoDataSource 'geosite.dat'

$xray = Join-Path $RuntimeBinSource 'xray.exe'
$wintun = Join-Path $RuntimeBinSource 'wintun.dll'
$marker = Join-Path $RuntimeBinSource 'dirac-core.sha256'

foreach($required in @($project,$amazTool,$VpkExe,$xray,$wintun,$marker,$geoip,$geosite)){
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
if((Get-FileHash -LiteralPath $geoip -Algorithm SHA256).Hash -ne $expectedGeoip){
    throw 'Dirac RU GeoIP SHA-256 mismatch; release refused.'
}
if((Get-FileHash -LiteralPath $geosite -Algorithm SHA256).Hash -ne $expectedGeosite){
    throw 'Dirac RU GeoSite SHA-256 mismatch; release refused.'
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
# Xray is configured with XRAY_LOCATION_ASSET pointing to the user bin root.
# Release-pinned, trimmed GeoData is mandatory for the RU-direct policy.
$assetDest = Join-Path $work 'bin'
Copy-Item -LiteralPath $geoip -Destination (Join-Path $assetDest 'geoip.dat') -Force
Copy-Item -LiteralPath $geosite -Destination (Join-Path $assetDest 'geosite.dat') -Force

$licenses = Join-Path $work 'licenses'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination (Join-Path $licenses 'GPL-3.0.txt') -Force
Copy-Item -LiteralPath (Join-Path $repo 'docs\ru-routing.md') -Destination (Join-Path $licenses 'Dirac-Russia-Geodata-NOTICE.md') -Force

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
Write-Output "GEOIP_SHA256=$expectedGeoip"
Write-Output "GEOSITE_SHA256=$expectedGeosite"