[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('x86')]
    [string]$Platform = 'x86',
    [string]$ReleaseRoot,
    [string]$BuildOutputDir,
    [string]$MSBuildPath = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe',
    [switch]$SkipBuild,
    [switch]$SkipZip
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectRoot = Join-Path $repoRoot 'readboard'
$solutionFile = Join-Path $repoRoot 'readboard.sln'
$nugetConfigPath = Join-Path $repoRoot 'NuGet.Config'
$projectFile = Join-Path $projectRoot 'readboard.csproj'
$assemblyInfoPath = Join-Path $projectRoot 'Properties\AssemblyInfo.cs'

if (-not $ReleaseRoot) {
    $ReleaseRoot = Join-Path $repoRoot 'release'
}

if (-not $BuildOutputDir) {
    $BuildOutputDir = Join-Path $projectRoot "bin\$Platform\$Configuration"
}

$buildPatterns = @(
    'readboard.exe',
    'readboard.exe.config',
    'readboard.pdb',
    'MouseKeyboardActivityMonitor.dll',
    'OpenCvSharp*.pdb',
    'OpenCvSharp*.xml'
)

$optionalStaticPatterns = @(
    'language_*.txt',
    'readme*.rtf'
)

$requiredStaticFiles = @(
    'lw.dll'
)

$managedRuntimeFiles = @(
    'OpenCvSharp.dll',
    'OpenCvSharp.Blob.dll',
    'OpenCvSharp.Extensions.dll',
    'OpenCvSharp.UserInterface.dll'
)

$nativeRuntimeFiles = @(
    'dll\x86\OpenCvSharpExtern.dll',
    'dll\x86\opencv_ffmpeg400.dll'
)

$requiredBuildFiles = @(
    'readboard.exe',
    'readboard.exe.config',
    'MouseKeyboardActivityMonitor.dll'
) + $managedRuntimeFiles + $nativeRuntimeFiles

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

function Invoke-MSBuildProcess {
    param(
        [string]$ResolvedMSBuildPath,
        [string[]]$Arguments,
        [string]$OperationName
    )

    $process = Start-Process -FilePath $ResolvedMSBuildPath -ArgumentList $arguments -NoNewWindow -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "$OperationName 失败，MSBuild 退出码: $($process.ExitCode)"
    }
}

function Invoke-LegacyPackageRestore {
    param(
        [string]$ResolvedMSBuildPath,
        [string]$ResolvedSolutionFile,
        [string]$ResolvedNuGetConfigPath
    )

    $arguments = @(
        $ResolvedSolutionFile,
        '/t:Restore',
        '/p:RestorePackagesConfig=true',
        "/p:RestoreConfigFile=$ResolvedNuGetConfigPath",
        '/nologo',
        '/v:m'
    )

    Invoke-MSBuildProcess -ResolvedMSBuildPath $ResolvedMSBuildPath -Arguments $arguments -OperationName '依赖还原'
}

function Invoke-ProjectBuild {
    param(
        [string]$ResolvedMSBuildPath,
        [string]$ResolvedProjectFile
    )

    $arguments = @(
        $ResolvedProjectFile,
        '/t:Build',
        "/p:Configuration=$Configuration",
        "/p:Platform=$Platform",
        '/p:TargetFrameworkVersion=v4.8',
        '/nologo',
        '/v:m'
    )

    Invoke-MSBuildProcess -ResolvedMSBuildPath $ResolvedMSBuildPath -Arguments $arguments -OperationName '构建'
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

function Copy-RelativeFiles {
    param(
        [string]$SourceDir,
        [string[]]$RelativePaths,
        [string]$DestinationDir
    )

    foreach ($relativePath in $RelativePaths) {
        $sourcePath = Join-Path $SourceDir $relativePath
        if (-not (Test-Path -LiteralPath $sourcePath)) {
            throw "构建输出不完整，缺少: $relativePath"
        }

        $destinationPath = Join-Path $DestinationDir $relativePath
        $destinationParent = Split-Path -Parent $destinationPath
        New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
        Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
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

Assert-PathExists -Path $MSBuildPath -Label 'MSBuild.exe'
if (-not $SkipBuild) {
    Assert-PathExists -Path $solutionFile -Label 'readboard.sln'
    Assert-PathExists -Path $nugetConfigPath -Label 'NuGet.Config'

    Write-Host "Restoring legacy packages with $MSBuildPath"
    Invoke-LegacyPackageRestore -ResolvedMSBuildPath $MSBuildPath -ResolvedSolutionFile $solutionFile -ResolvedNuGetConfigPath $nugetConfigPath

    Write-Host "Building $($versionInfo.TagVersion) with $MSBuildPath"
    Invoke-ProjectBuild -ResolvedMSBuildPath $MSBuildPath -ResolvedProjectFile $projectFile
}

Assert-RequiredFiles -SourceDir $BuildOutputDir -RequiredFiles $requiredBuildFiles

if (Test-Path -LiteralPath $releaseDirectory) {
    Remove-Item -LiteralPath $releaseDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
Copy-MatchingFiles -SourceDir $BuildOutputDir -Patterns $buildPatterns -DestinationDir $releaseDirectory
Copy-MatchingFiles -SourceDir $projectRoot -Patterns $optionalStaticPatterns -DestinationDir $releaseDirectory
Copy-RelativeFiles -SourceDir $projectRoot -RelativePaths $requiredStaticFiles -DestinationDir $releaseDirectory
Copy-RelativeFiles -SourceDir $BuildOutputDir -RelativePaths $managedRuntimeFiles -DestinationDir $releaseDirectory
Copy-RelativeFiles -SourceDir $BuildOutputDir -RelativePaths $nativeRuntimeFiles -DestinationDir $releaseDirectory
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
