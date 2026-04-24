param(
    [string]$Configuration = "Debug",
    [string]$Language = "cn",
    [string]$ExePath
)

$ErrorActionPreference = "Stop"

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $scriptDirectory "..")).Path
$projectPath = Join-Path $repoRoot "readboard\readboard.csproj"

function Get-TargetFramework {
    param([string]$ProjectFile)

    if (-not (Test-Path -LiteralPath $ProjectFile -PathType Leaf)) {
        throw "Project file not found: $ProjectFile"
    }

    [xml]$csproj = Get-Content -LiteralPath $ProjectFile
    $tfm = $csproj.Project.PropertyGroup.TargetFramework
    if ($tfm -is [array]) { $tfm = ($tfm | Where-Object { $_ } | Select-Object -First 1) }
    if ([string]::IsNullOrWhiteSpace($tfm)) {
        throw "Could not read <TargetFramework> from $ProjectFile"
    }
    return $tfm.Trim()
}

if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $targetFramework = Get-TargetFramework -ProjectFile $projectPath
    $exePath = Join-Path $repoRoot "readboard\bin\$Configuration\$targetFramework\readboard.exe"

    if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
        Write-Host "Building readboard $Configuration..."
        dotnet build $projectPath -c $Configuration
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
        if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
            throw "Build completed but readboard.exe was not found: $exePath"
        }
    }
} else {
    if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
        throw "readboard.exe not found: $ExePath"
    }

    $exePath = (Resolve-Path -LiteralPath $ExePath).Path
}

$arguments = @("yzy", " ", " ", " ", "0", $Language, "-1")
$workingDirectory = Split-Path -Parent $exePath

Write-Host "Starting readboard with LizzieYzy-Next-style debug arguments..."
Write-Host "`"$exePath`" $($arguments -join ' ')"

$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $exePath
$startInfo.WorkingDirectory = $workingDirectory
$startInfo.UseShellExecute = $false
foreach ($argument in $arguments) {
    [void]$startInfo.ArgumentList.Add($argument)
}

[void][System.Diagnostics.Process]::Start($startInfo)
