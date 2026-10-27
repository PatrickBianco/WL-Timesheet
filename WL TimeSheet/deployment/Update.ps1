#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Downloads a newer published WL TimeSheet build and restarts the Windows service.

.DESCRIPTION
    Reads update.settings.json beside this script. Checks the GitHub release tagged with
    a version, and when that version is newer than the installed EXE, stops WLTimeSheet,
    keeps the current EXE, copies the new program files, and starts the service.

    These files are never replaced: appsettings.Production.json, update.settings.json,
    certificate files, logs, encryption keys, and the database.
#>
[CmdletBinding()]
param(
    [switch]$Initialize,
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'
$serviceName = 'WLTimeSheet'
$executableName = 'WL TimeSheet.exe'
$root = $PSScriptRoot
$executable = Join-Path $root $executableName
$settingsPath = Join-Path $root 'update.settings.json'
$replaceNames = @($executableName, 'appsettings.json', 'Service.ps1', 'Update.ps1', 'README.md')

function Get-ReleaseVersion([string]$Name) {
    $parsed = $Name.Trim().TrimStart('v', 'V')
    $version = [version]'0.0.0.0'
    if (-not [version]::TryParse($parsed, [ref]$version)) {
        throw "Release '$Name' is not a version such as 1.0.0.2."
    }
    return $version
}

function Get-InstalledVersion {
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        return [version]'0.0.0.0'
    }
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable)
    $text = if ($info.FileVersion) { ($info.FileVersion -split '\s+')[0] } else { '0.0.0.0' }
    return Get-ReleaseVersion $text
}

if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) {
    throw "Create $settingsPath from update.settings.example.json and set the GitHub repository."
}
$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($settings.Repository) -or $settings.Repository -notmatch '^[^/\s]+/[^/\s]+$') {
    throw 'Set Repository in update.settings.json to owner/name.'
}
$assetName = if ([string]::IsNullOrWhiteSpace($settings.AssetName)) { 'WLTimeSheet-win-x64.zip' } else { $settings.AssetName }

$headers = @{
    Accept = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
    'User-Agent' = 'WLTimeSheet-Update'
}
if (-not [string]::IsNullOrWhiteSpace($settings.TokenPath)) {
    $tokenPath = [Environment]::ExpandEnvironmentVariables($settings.TokenPath)
    if (-not (Test-Path -LiteralPath $tokenPath -PathType Leaf)) {
        throw "GitHub token file not found: $tokenPath"
    }
    $headers.Authorization = 'Bearer ' + (Get-Content -LiteralPath $tokenPath -Raw).Trim()
}

$release = Invoke-RestMethod -Headers $headers -Uri "https://api.github.com/repos/$($settings.Repository)/releases/latest"
$remoteVersion = Get-ReleaseVersion $release.tag_name
$installedVersion = Get-InstalledVersion
Write-Output "Installed $installedVersion. Latest release $remoteVersion."
if ($remoteVersion -le $installedVersion) {
    Write-Output 'No update.'
    return
}

$asset = @($release.assets) | Where-Object { $_.name -eq $assetName } | Select-Object -First 1
if (-not $asset) {
    throw "Release $($release.tag_name) has no asset named $assetName."
}

$downloadHeaders = @{
    Accept = 'application/octet-stream'
    'X-GitHub-Api-Version' = '2022-11-28'
    'User-Agent' = 'WLTimeSheet-Update'
}
if ($headers.ContainsKey('Authorization')) {
    $downloadHeaders.Authorization = $headers.Authorization
}
$work = Join-Path ([IO.Path]::GetTempPath()) ("WLTimeSheet-Update-" + [guid]::NewGuid().ToString('n'))
New-Item -ItemType Directory -Path $work | Out-Null
$package = Join-Path $work $assetName
try {
    Write-Output "Downloading $($asset.name)."
    Invoke-WebRequest -Headers $downloadHeaders -Uri $asset.url -OutFile $package
    $extracted = Join-Path $work 'package'
    Expand-Archive -LiteralPath $package -DestinationPath $extracted -Force
    $incomingExe = Get-ChildItem -LiteralPath $extracted -Filter $executableName -Recurse | Select-Object -First 1
    if (-not $incomingExe) {
        throw "The release package does not contain $executableName."
    }
    $packageRoot = $incomingExe.Directory.FullName
    $packageVersion = Get-ReleaseVersion (([Diagnostics.FileVersionInfo]::GetVersionInfo($incomingExe.FullName).FileVersion -split '\s+')[0])
    if ($packageVersion -ne $remoteVersion) {
        throw "Package version $packageVersion does not match release $remoteVersion."
    }

    if ($WhatIf) {
        Write-Output "Would stop $serviceName, install $packageVersion, and start $serviceName."
        return
    }

    $backupDir = Join-Path $root ("releases\" + $installedVersion.ToString())
    New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
    foreach ($name in $replaceNames) {
        $current = Join-Path $root $name
        if (Test-Path -LiteralPath $current -PathType Leaf) {
            Copy-Item -LiteralPath $current -Destination (Join-Path $backupDir $name) -Force
        }
    }

    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    $wasRunning = $service -and $service.Status -ne 'Stopped'
    if ($wasRunning) {
        Stop-Service -Name $serviceName -Force
        (Get-Service -Name $serviceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
    }
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Process -Name 'WL TimeSheet' -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 1
    }
    if (Get-Process -Name 'WL TimeSheet' -ErrorAction SilentlyContinue) {
        throw 'The timesheet process is still running, so the EXE cannot be replaced.'
    }

    try {
        foreach ($name in $replaceNames) {
            $source = Join-Path $packageRoot $name
            if ($name -eq $executableName -or (Test-Path -LiteralPath $source -PathType Leaf)) {
                if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
                    throw "Missing $name in the release package."
                }
                Copy-Item -LiteralPath $source -Destination (Join-Path $root $name) -Force
            }
        }
        if ($Initialize) {
            & $executable --environment Production --initialize
            if ($LASTEXITCODE -ne 0) {
                throw 'Database initialization failed.'
            }
        }
        if ($service) {
            Start-Service -Name $serviceName
            (Get-Service -Name $serviceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(60))
            Start-Sleep -Seconds 5
            if ((Get-Service -Name $serviceName).Status -ne 'Running') {
                throw 'The service stopped after starting. A database upgrade may be required.'
            }
        }
        Write-Output "Updated to $packageVersion."
    }
    catch {
        Write-Warning "Update failed. Restoring $installedVersion."
        foreach ($name in $replaceNames) {
            $saved = Join-Path $backupDir $name
            if (Test-Path -LiteralPath $saved -PathType Leaf) {
                Copy-Item -LiteralPath $saved -Destination (Join-Path $root $name) -Force
            }
        }
        if ($service) {
            try { Start-Service -Name $serviceName } catch { Write-Warning $_.Exception.Message }
        }
        throw
    }
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
