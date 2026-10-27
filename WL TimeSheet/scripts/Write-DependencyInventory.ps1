$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$assets = Get-Content -LiteralPath (Join-Path $projectRoot 'obj/project.assets.json') -Raw | ConvertFrom-Json
$packageRoot = @($assets.packageFolders.PSObject.Properties)[0].Name
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# Resolved application dependency inventory')
$lines.Add('')
$lines.Add('Generated from restored NuGet metadata. Runtime and design-time packages are included; packages.lock.json pins exact versions. Significant dependency usage is summarized in THIRD_PARTY_LIBRARIES.md. SNI is resolved by NuGet but all assets are excluded from use/publication.')
$lines.Add('')
$lines.Add('| Library | Version | License | Official project/repository | Used by |')
$lines.Add('|---|---|---|---|---|')
foreach ($lib in $assets.libraries.PSObject.Properties) {
    if ($lib.Value.type -ne 'package') { continue }
    $parts = $lib.Name.Split('/')
    $nuspec = Join-Path $packageRoot ($lib.Value.path + '/' + $parts[0].ToLowerInvariant() + '.nuspec')
    [xml]$doc = Get-Content -LiteralPath $nuspec
    $metadata = $doc.package.metadata
    $license = $metadata.license.InnerText
    $repo = $metadata.repository.url
    if (-not $repo) { $repo = $metadata.projectUrl }
    if (-not $repo -and $parts[0] -eq 'RBush.Signed') { $repo = 'https://github.com/viceroypenguin/RBush' }
    $used = 'Framework/runtime support'
    if ($parts[0] -match 'EntityFramework|SqlClient|SqlServer') { $used = 'AppDbContext / SQL persistence' }
    if ($parts[0] -match 'IdentityModel|OpenIdConnect') { $used = 'Transitive SQL driver identity support (no application OIDC login)' }
    if ($parts[0] -match 'Negotiate|DirectoryServices') { $used = 'Windows authentication' }
    if ($parts[0] -match 'WindowsServices|ServiceController|EventLog') { $used = 'Windows Service hosting and Event Log integration' }
    if ($parts[0] -match 'ClosedXML|OpenXml|ExcelNumber|RBush|SixLabors|Packaging') { $used = 'ExcelExportService via ClosedXML' }
    if ($parts[0] -match 'CodeAnalysis|CodeDom|Composition|Humanizer|TextTemplating|Build|SolutionPersistence|EntityFrameworkCore.Design') { $used = 'EF migration design tooling' }
    if ($parts[0] -eq 'Microsoft.Data.SqlClient.SNI.runtime') { $license = 'Proprietary - EXCLUDED'; $used = 'Not used or shipped; ExcludeAssets=all' }
    if (-not $license) { throw ('Unverified license: ' + $lib.Name) }
    if ($license -notin @('MIT','Apache-2.0','BSD-2-Clause','BSD-3-Clause','Proprietary - EXCLUDED')) { throw ('Review required for license: ' + $lib.Name + ' ' + $license) }
    $lines.Add('| ' + $parts[0] + ' | ' + $parts[1] + ' | ' + $license + ' | ' + $repo + ' | ' + $used + ' |')
}
$lines.Add('| Lucide SVG subset | 0.468.0 | ISC / Feather MIT | https://github.com/lucide-icons/lucide/tree/0.468.0 | 18 local SVG UI icons; license in wwwroot/icons/lucide/LICENSE |')
$lines | Set-Content -LiteralPath (Join-Path $projectRoot 'docs/DEPENDENCY_INVENTORY.md') -Encoding UTF8
