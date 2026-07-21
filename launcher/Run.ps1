#Requires -Version 5.0
param(
    [string]$ConfigPath  = $null,
    [switch]$BootstrapOnly
)

$ErrorActionPreference  = "Stop"
$ProgressPreference     = "SilentlyContinue"

$ScriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Definition
$BonsaiDir  = Join-Path $ScriptDir ".bonsai"
$BonsaiExe  = Join-Path $BonsaiDir "Bonsai.exe"

if (-not (Test-Path $BonsaiExe)) {
    $bonsaiConfigPath = Join-Path $BonsaiDir "Bonsai.config"
    $release = "https://github.com/bonsai-rx/bonsai/releases/latest/download/Bonsai.zip"

    if (Test-Path $bonsaiConfigPath) {
        [xml]$bonsaiConfig = Get-Content $bonsaiConfigPath
        $bootstrapper = $bonsaiConfig.PackageConfiguration.Packages.Package |
            Where-Object { $_.id -eq "Bonsai" }
        if ($bootstrapper) {
            $release = "https://github.com/bonsai-rx/bonsai/releases/download/$($bootstrapper.version)/Bonsai.zip"
        }
    }

    $zipPath     = Join-Path $BonsaiDir "temp.zip"
    $nugetConfig = Join-Path $BonsaiDir "NuGet.config"
    $nugetBackup = if (Test-Path $nugetConfig) { Get-Content $nugetConfig -Raw } else { $null }

    Write-Host "  Downloading $release ..."
    Invoke-WebRequest $release -OutFile $zipPath
    Expand-Archive -Path $zipPath -DestinationPath $BonsaiDir -Force
    Remove-Item $zipPath

    if ($null -ne $nugetBackup) {
        Set-Content -Path $nugetConfig -Value $nugetBackup -Encoding UTF8
    }

    Write-Host "Bonsai installed."
}

if ($BootstrapOnly) {
    & $BonsaiExe --no-editor
    exit 0
}

$WorkflowFile = @(
    (Join-Path $ScriptDir "MiniscopeGui.bonsai"),
    (Join-Path $ScriptDir "..\OpenEphys.MiniscopeV4.Gui\Workflows\MiniscopeGui.bonsai")
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $WorkflowFile) {
    Write-Error "MiniscopeGui.bonsai not found.`nExpected at: $ScriptDir\MiniscopeGui.bonsai"
    exit 1
}

$WorkflowFile = [System.IO.Path]::GetFullPath($WorkflowFile)

$bonsaiArgs = @(
    $WorkflowFile
    "--no-editor"
)

$bonsaiArgs += "-p:StopWorkflowOnClose=true"
$bonsaiArgs += "-p:ConfigFilePath=./config.yml"

Write-Host "Starting Miniscope GUI..."
& $BonsaiExe @bonsaiArgs
exit $LASTEXITCODE
