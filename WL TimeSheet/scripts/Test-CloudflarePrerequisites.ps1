param(
    [string]$CloudflaredPath = 'C:\Cloudflare\cloudflared-windows-amd64.exe',
    [int]$HttpsPort = 7294
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $CloudflaredPath -PathType Leaf)) { throw 'cloudflared executable was not found.' }
if ($HttpsPort -lt 1 -or $HttpsPort -gt 65535) { throw 'Invalid local HTTPS port.' }
& $CloudflaredPath --version
if ($LASTEXITCODE -ne 0) { throw 'cloudflared could not run.' }
$configurationDirectory = Join-Path $env:USERPROFILE '.cloudflared'
$configurationFiles = @()
if (Test-Path -LiteralPath $configurationDirectory) {
    # Report filenames only. Never display certificates, credentials or tunnel tokens.
    $configurationFiles = @(Get-ChildItem -LiteralPath $configurationDirectory -File | Select-Object -ExpandProperty Name)
}
[PSCustomObject]@{
    Check = 'Default user tunnel configuration'
    Result = if ($configurationFiles.Count) { $configurationFiles -join ', ' } else { 'No files found; dashboard/service configuration may exist elsewhere.' }
}
# Certificate bypass applies only to the loopback development certificate for this probe.
$status = & curl.exe --http1.1 --insecure --silent --show-error --max-time 10 --output NUL --write-out '%{http_code}' "https://localhost:$HttpsPort/timesheet"
if ($LASTEXITCODE -ne 0) { throw 'The local app is unreachable. Start its https launch profile first.' }
[PSCustomObject]@{ Check = 'Anonymous local timesheet request'; Result = "HTTP $status" }
if ($status -ne '401') { throw 'Expected Windows authentication challenge (401). Review the running app before configuring remote access.' }
Write-Output 'Local app is reachable and requires authentication. No tunnel has been created or started.'
Write-Output 'Windows Negotiate needs a connection-preserving private route. A browser-only review link needs an explicitly configured web identity provider and employee mapping.'
