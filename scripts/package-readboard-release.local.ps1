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

if (-not $ReleaseRoot) {
    $ReleaseRoot = Join-Path $repoRoot 'release'
}

if (-not $BuildOutputDir) {
    $BuildOutputDir = Join-Path $projectRoot "bin\$Configuration\net10.0-windows"
}

$buildPatterns = @(
    'readboard.exe',
    'readboard.dll',
    'readboard.pdb',
    'readboard.runtimeconfig.json',
    'readboard.deps.json',
    'OpenCvSharp*.dll',
    'OpenCvSharp*.pdb'
)

$optionalStaticPatterns = @(
    'language_*.txt',
    'readme*.rtf'
)

$nativeRuntimePatterns = @(
    'OpenCvSharpExtern.dll'
)

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

function Copy-MatchingFiles {
    param(
        [string]$SourceDir,
        [string[]]$Patterns,
        [string]$DestinationDir
    )

    foreach ($pattern in $Patterns) {
        foreach ($item in Get-ChildItem -LiteralPath $SourceDir -Filter $pattern -File -ErrorAction SilentlyContinue) {
            Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $DestinationDir $item.Name) -Force
        }
    }
}

function Copy-NativeRuntimeFiles {
    param(
        [string]$SourceDir,
        [string[]]$Patterns,
        [string]$DestinationDir
    )

    foreach ($pattern in $Patterns) {
        foreach ($item in Get-ChildItem -Path $SourceDir -Filter $pattern -Recurse -File -ErrorAction SilentlyContinue) {
            Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $DestinationDir $item.Name) -Force
        }
    }
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

if (-not $SkipBuild) {
    Write-Host "Building $($versionInfo.TagVersion) with dotnet"
    dotnet build $projectFile -c $Configuration --nologo -v m
    if ($LASTEXITCODE -ne 0) {
        throw "构建失败，dotnet 退出码: $LASTEXITCODE"
    }
}

Assert-RequiredFiles -SourceDir $BuildOutputDir -RequiredFiles $requiredBuildFiles

if (Test-Path -LiteralPath $releaseDirectory) {
    Remove-Item -LiteralPath $releaseDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
Copy-MatchingFiles -SourceDir $BuildOutputDir -Patterns $buildPatterns -DestinationDir $releaseDirectory
Copy-MatchingFiles -SourceDir $projectRoot -Patterns $optionalStaticPatterns -DestinationDir $releaseDirectory
Copy-NativeRuntimeFiles -SourceDir $BuildOutputDir -Patterns $nativeRuntimePatterns -DestinationDir $releaseDirectory
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
