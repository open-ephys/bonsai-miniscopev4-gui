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
$ConfigFile = if ($ConfigPath) { $ConfigPath } else { Join-Path $ScriptDir "config.json" }

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

if (Test-Path $ConfigFile) {
    $config = Get-Content $ConfigFile -Raw | ConvertFrom-Json
    foreach ($prop in $config.PSObject.Properties) {
        if ($prop.Name -like "_*") { continue }

        $value = $prop.Value

        if ($value -is [bool]) { $value = $value.ToString().ToLower() }

        if ($null -eq $value -or "$value" -eq "") { continue }

        if ($prop.Name -eq "FileName" -and "$value".StartsWith("~")) {
            $value = ($env:USERPROFILE + ("$value".Substring(1))).Replace("\", "/")
        }

        $bonsaiArgs += "-p:$($prop.Name)=$value"
    }
} else {
    Write-Warning "config.json not found at '$ConfigFile'; default values will be used."
}

$bonsaiArgs += "-p:StopWorkflowOnClose=true"

Write-Host "Starting Miniscope GUI..."
& $BonsaiExe @bonsaiArgs
exit $LASTEXITCODE
