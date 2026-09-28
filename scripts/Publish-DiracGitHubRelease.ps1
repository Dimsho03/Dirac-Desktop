[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [ValidateSet('stable','beta')]
    [string]$Channel = 'stable',

    [Parameter(Mandatory)]
    [string]$FeedDir,

    [string]$NotesFile = '',

    # Dry-run by default. Opting in creates a draft, not a public release.
    [switch]$PublishDraft
)

$ErrorActionPreference = 'Stop'
$repoUrl = 'https://github.com/Dimsho03/Dirac-Desktop'
$repoSlug = 'Dimsho03/Dirac-Desktop'
$packageId = 'Dimsho.Dirac.Desktop'
$channelId = "win-x64-$Channel"
$feed = [IO.Path]::GetFullPath($FeedDir)
$manifest = Join-Path $feed "releases.$channelId.json"
$assetsFile = Join-Path $feed "assets.$channelId.json"
$legacyFeed = Join-Path $feed "RELEASES-$channelId"
$tag = "v$Version"

foreach($file in @($manifest,$assetsFile,$legacyFeed)){
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){
        throw "Required Velopack metadata is missing: $file"
    }
}

$releases = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
$versioned = @($releases.Assets | Where-Object {
    $_.Version -eq $Version -and $_.PackageId -eq $packageId
})
if(@($versioned | Where-Object Type -eq 'Full').Count -ne 1){
    throw 'The requested release must contain exactly one full package with the expected ID and version.'
}

$assetList = @(Get-Content -LiteralPath $assetsFile -Raw | ConvertFrom-Json)
if($assetList.Count -lt 2){throw 'The Velopack assets manifest is incomplete.'}
$assetNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($asset in $assetList){
    $name = [string]$asset.RelativeFileName
    if([string]::IsNullOrWhiteSpace($name) -or
       $name -ne [IO.Path]::GetFileName($name) -or
       $name -match '(?i)(private|secret|profile|guiConfigs|binConfigs|token|password)'){
        throw 'A Velopack asset name is unsafe or is not a local file name.'
    }
    if(-not $assetNames.Add($name)){throw "Duplicate release asset: $name"}
}

$fullName="$packageId-$Version-$channelId-full.nupkg"
if(-not $assetNames.Contains($fullName)){
    throw "Velopack assets do not include the expected full package: $fullName"
}
$setupName="$packageId-$channelId-Setup.exe"
if(-not $assetNames.Contains($setupName)){
    throw "Velopack assets do not include the expected installer: $setupName"
}

$files = [Collections.Generic.List[string]]::new()
foreach($name in $assetNames){
    $path = Join-Path $feed $name
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){
        throw "Velopack asset is missing: $name"
    }
    $files.Add($path)
}

foreach($releaseAsset in $versioned){
    $name = [string]$releaseAsset.FileName
    $path = Join-Path $feed $name
    if(-not $assetNames.Contains($name) -or -not(Test-Path -LiteralPath $path -PathType Leaf)){
        throw "The current release's package or manifest is missing: $name"
    }
    $file = Get-Item -LiteralPath $path
    if($file.Length -ne [long]$releaseAsset.Size){
        throw "Package length differs from its Velopack manifest: $name"
    }
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if($actual -ne [string]$releaseAsset.SHA256){
        throw "Package SHA-256 differs from its Velopack manifest: $name"
    }
}

# Independently prevent user-owned and mutable configuration files from
# entering a distributable package if the packaging script changes later.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $feed $fullName))
try{
    $offenders=@($archive.Entries | Where-Object {
        $_.FullName -match '(?i)(^|/|\\)(guiConfigs|binConfigs|profiles|private)(/|\\)' -or
        $_.FullName -match '(?i)\.(db|sqlite|pem|p12|key)$'
    })
    if($offenders.Count -gt 0){
        throw 'Private or mutable configuration files would enter the GitHub release.'
    }
}finally{
    $archive.Dispose()
}

$files.Add($manifest)
$files.Add($assetsFile)
$files.Add($legacyFeed)
$files.Sort([StringComparer]::OrdinalIgnoreCase)
Write-Output "DIRAC_GITHUB_RELEASE_PREFLIGHT_PASS=true"
Write-Output "REPO=$repoUrl"
Write-Output "TAG=$tag"
Write-Output "CHANNEL=$channelId"
Write-Output "ASSET_COUNT=$($files.Count)"
Write-Output "PACKAGE_HASH_VERIFIED=true"
Write-Output "PRIVATE_PAYLOAD_SCAN_PASS=true"

if(-not $PublishDraft){
    Write-Output 'DRY_RUN_ONLY=true'
    Write-Output 'No GitHub release, tag or repository settings were modified.'
    return
}

if([string]::IsNullOrWhiteSpace($NotesFile) -or
   -not(Test-Path -LiteralPath $NotesFile -PathType Leaf)){
    throw 'A reviewed release-notes file is required before uploading a draft.'
}

$setup = Join-Path $feed $setupName
$signature = Get-AuthenticodeSignature -LiteralPath $setup
if($signature.Status -ne 'Valid'){
    throw 'The installer is unsigned or its Authenticode signature is invalid.'
}

$gh = Get-Command gh -ErrorAction SilentlyContinue
if($null -eq $gh){throw 'GitHub CLI is required. Authenticate it without embedding a token.'}
$repoInfo = & $gh.Source repo view $repoSlug --json isPrivate | ConvertFrom-Json
if($LASTEXITCODE -ne 0){throw 'Cannot verify GitHub repository visibility.'}
if($repoInfo.isPrivate){
    throw 'The source repo is private. Anonymous Dirac clients cannot fetch releases.'
}

# Immutable tags: no accidental overwrite of an existing published release.
& $gh.Source release view $tag --repo $repoSlug --json tagName *> $null
if($LASTEXITCODE -eq 0){throw "Tag $tag already has a GitHub release; no overwrite permitted."}

$mainSha = (git -C (Split-Path -Parent $PSScriptRoot) rev-parse HEAD).Trim()
if($LASTEXITCODE -ne 0 -or $mainSha.Length -ne 40){throw 'Cannot identify the source commit.'}

$args = @(
    'release','create',$tag
) + $files.ToArray() + @(
    '--repo',$repoSlug,
    '--target',$mainSha,
    '--title',"Dirac Desktop $Version ($Channel)",
    '--notes-file',$NotesFile,
    '--draft'
)
if($Channel -eq 'beta'){$args += '--prerelease'}
else{$args += '--latest'}
& $gh.Source @args
if($LASTEXITCODE -ne 0){throw 'GitHub draft release creation failed.'}
Write-Output 'GITHUB_DRAFT_CREATED=true'
Write-Output 'Review assets and the source tag before publishing.'
