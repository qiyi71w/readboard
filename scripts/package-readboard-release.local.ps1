[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release',
    [string]$ReleaseRoot,
    [string]$BuildOutputDir,
    [switch]$SkipBuild,
    [switch]$SkipZip
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectRoot = Join-Path $repoRoot 'readboard'
$projectFile = Join-Path $projectRoot 'readboard.csproj'
$assemblyInfoPath = Join-Path $projectRoot 'Properties\AssemblyInfo.cs'

$publishRuntimeIdentifier = 'win-x64'
$publishTargetFramework = 'net10.0-windows'

if (-not $ReleaseRoot) {
    $ReleaseRoot = Join-Path $repoRoot 'release'
}

if (-not $BuildOutputDir) {
    $BuildOutputDir = Join-Path $projectRoot "bin\$Configuration\$publishTargetFramework\$publishRuntimeIdentifier\publish"
}

$requiredBuildFiles = @(
    'readboard.exe',
    'readboard.dll'
)

function Get-ReleaseVersion {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "未找到版本文件: $Path"
    }

    $content = Get-Content -LiteralPath $Path -Raw
    $version = $null
    if ($content -match '\[assembly:\s*AssemblyInformationalVersion\("([^"]+)"\)\]') {
        $version = $Matches[1]
    } elseif ($content -match '\[assembly:\s*AssemblyFileVersion\("([^"]+)"\)\]') {
        $version = $Matches[1]
    } elseif ($content -match '\[assembly:\s*AssemblyVersion\("([^"]+)"\)\]') {
        $version = $Matches[1]
    }

    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "无法从 AssemblyInfo 读取版本号: $Path"
    }

    $tagVersion = $version
    if (-not $tagVersion.StartsWith('v', [System.StringComparison]::OrdinalIgnoreCase)) {
        $tagVersion = "v$tagVersion"
    }

    return @{
        TagVersion = $tagVersion
        NumericVersion = $tagVersion.TrimStart('v', 'V')
    }
}

function Assert-PathExists {
    param([string]$Path, [string]$Label)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "未找到${Label}: $Path"
    }
}

function Assert-RequiredFiles {
    param(
        [string]$SourceDir,
        [string[]]$RequiredFiles
    )

    $missing = foreach ($name in $RequiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $SourceDir $name))) {
            $name
        }
    }

    if ($missing) {
        throw "构建输出不完整，缺少: $($missing -join ', ')"
    }
}

function Copy-DirectoryContents {
    param(
        [string]$SourceDir,
        [string]$DestinationDir
    )

    Copy-Item -Path (Join-Path $SourceDir '*') -Destination $DestinationDir -Recurse -Force
}

function Update-ReleaseArtifactTimestamps {
    param(
        [string]$ReleaseDirectory,
        [DateTime]$TimestampUtc
    )

    foreach ($file in Get-ChildItem -LiteralPath $ReleaseDirectory -Recurse -File) {
        $file.LastWriteTimeUtc = $TimestampUtc
    }

    (Get-Item -LiteralPath $ReleaseDirectory).LastWriteTimeUtc = $TimestampUtc
}

function New-ReleaseArchive {
    param(
        [string]$SourceDirectory,
        [string]$DestinationZipPath
    )

    if (Test-Path -LiteralPath $DestinationZipPath) {
        Remove-Item -LiteralPath $DestinationZipPath -Force
    }

    [System.IO.Compression.ZipFile]::CreateFromDirectory($SourceDirectory, $DestinationZipPath)
}

$versionInfo = Get-ReleaseVersion -Path $assemblyInfoPath
$releaseDirectoryName = "readboard-github-release-$($versionInfo.TagVersion)"
$releaseDirectory = Join-Path $ReleaseRoot $releaseDirectoryName
$releaseZipPath = Join-Path $ReleaseRoot ($releaseDirectoryName + '.zip')
$resolvedReleaseZipPath = $releaseZipPath
$releaseAppDirectory = Join-Path $releaseDirectory 'readboard'

if (-not $SkipBuild) {
    Write-Host "Publishing $($versionInfo.TagVersion) with dotnet (self-contained, $publishRuntimeIdentifier)"
    dotnet publish $projectFile -c $Configuration -r $publishRuntimeIdentifier --self-contained true --nologo -v m
    if ($LASTEXITCODE -ne 0) {
        throw "发布失败，dotnet 退出码: $LASTEXITCODE"
    }
}

Assert-PathExists -Path $BuildOutputDir -Label '发布输出目录（dotnet publish 未产出或 BuildOutputDir 路径配置错误）'
Assert-RequiredFiles -SourceDir $BuildOutputDir -RequiredFiles $requiredBuildFiles

if (Test-Path -LiteralPath $releaseDirectory) {
    Remove-Item -LiteralPath $releaseDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $releaseAppDirectory -Force | Out-Null
Copy-DirectoryContents -SourceDir $BuildOutputDir -DestinationDir $releaseAppDirectory
$packageTimestampUtc = [DateTime]::UtcNow
Update-ReleaseArtifactTimestamps -ReleaseDirectory $releaseDirectory -TimestampUtc $packageTimestampUtc
if ($SkipZip) {
    if (Test-Path -LiteralPath $resolvedReleaseZipPath) {
        Remove-Item -LiteralPath $resolvedReleaseZipPath -Force
    }
    $releaseZipPath = [string]::Empty
}
if (-not $SkipZip) {
    New-ReleaseArchive -SourceDirectory $releaseDirectory -DestinationZipPath $resolvedReleaseZipPath
}

Write-Host "PackageDir=$releaseDirectory"
Write-Host "PackageZip=$releaseZipPath"
Write-Host "PackageVersion=$($versionInfo.TagVersion)"
