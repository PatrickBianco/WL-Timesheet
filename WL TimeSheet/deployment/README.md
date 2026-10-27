# WL TimeSheet — Windows x64 deployment

This folder contains the self-contained application:

- `WL TimeSheet.exe`: application, .NET runtime, frontend assets and Excel templates.
- `appsettings.json`: common application settings.
- `appsettings.Production.json`: server-specific configuration to edit before starting.
- `Service.ps1`: install, start, stop, restart, check or remove the Windows Service.
- `README.md`: these instructions.

No .NET installation or IIS is required. SQL Server is a separate prerequisite; it is not bundled. The EXE extracts its bundled files to the service account's private temporary directory at startup. Keep that directory writable. Logs and encryption keys go under `%ProgramData%\WLTimeSheet`, so running the application does not add files to this deployment folder.

## Command walkthrough: installation through browser access

These commands are **PowerShell**, not Command Prompt commands. Run them on the application server in **Windows PowerShell > Run as administrator**. Run each step separately and resolve any error before continuing. SQL blocks belong in **SSMS > New Query**, not PowerShell. Do not copy the `PS C:\...>` prompt or Markdown fence lines.

This walkthrough uses `C:\WL-TImesheet`, local `SQLEXPRESS`, SQL port `14330`, HTTPS port `443`, and the LocalService account. Confirm the hostname with `hostname`; replace `WSLTimesheet` in configuration and browser commands if the agreed DNS name differs. A remote SQL server requires a suitable domain service identity instead.

For the existing installation, the database initialization and service installation have already succeeded. Skip those steps; apply only the pending configuration/permission changes and restart.

### 1. Copy the package and check the server

Copy all five published files into `C:\WL-TImesheet`. Retain existing production settings if updating. Then run:

```powershell
Set-Location 'C:\WL-TImesheet'
hostname
whoami
Get-ChildItem -File
Get-Service -Name 'MSSQL$SQLEXPRESS', 'WLTimeSheet' -ErrorAction SilentlyContinue
Get-NetTCPConnection -LocalPort 443 -State Listen -ErrorAction SilentlyContinue |
    Select-Object LocalAddress, LocalPort, OwningProcess
```

If port 443 is occupied, identify the process with `Get-Process -Id <OwningProcess>` before proceeding. Do not stop another application's listener. SQL Server must already be installed; the timesheet EXE does not install SQL Server.

### 2. Configure SQL TCP access

In **SQL Server Configuration Manager > SQL Server Network Configuration > Protocols for SQLEXPRESS**, enable TCP/IP. In TCP/IP Properties, set **Listen All = Yes**. On the IP Addresses tab under **IPAll**, clear **TCP Dynamic Ports** completely (remove `0`) and set **TCP Port = 14330**. Apply the changes.

Only if you changed these settings, restart this SQL instance during an appropriate maintenance window; its existing connections will be interrupted:

```powershell
Restart-Service -Name 'MSSQL$SQLEXPRESS'
Test-NetConnection 127.0.0.1 -Port 14330
```

Continue when `TcpTestSucceeded` is `True`. A direct TCP connection using an explicit port does not require SQL Browser discovery.

### 3. Configure HTTPS, SQL and persistent folders

Back up and open the actual server configuration:

```powershell
$configPath = 'C:\WL-TImesheet\appsettings.Production.json'
Copy-Item -LiteralPath $configPath -Destination ($configPath + '.' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.bak')
notepad.exe $configPath
```

Edit the JSON in Notepad, not in the PowerShell prompt. Set:

- `Kestrel.Endpoints.Https.Url`: `https://0.0.0.0:443`
- `AllowedHosts`: the actual browser hostname(s), separated by semicolons, without ports; for the name used during setup: `localhost;WSLTimesheet`.
- HTTPS certificate: the matching subject in `My`, `LocalMachine`, with `AllowInvalid: false`.
- `Logging.DiagnosticFile.Directory`: `%ProgramData%\\WLTimeSheet\\Logs` in JSON.
- `DataProtection.KeyDirectory`: `%ProgramData%\\WLTimeSheet\\Keys\\LocalService` in JSON.
- SQL connection: the correct server, database and Windows integrated authentication.

The temporary same-machine SQL connection used during troubleshooting is shown below. It bypasses SQL certificate validation; use a trusted SQL certificate and matching SQL hostname with `TrustServerCertificate=False` for the final validated configuration.

```json
"Timesheet": "Server=tcp:127.0.0.1,14330;Database=WLTimeSheet;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
```

Validate the saved JSON and create the configured service folders:

```powershell
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$config.Kestrel.Endpoints.Https.Url
$config.DataProtection.KeyDirectory
foreach ($folder in @($config.Logging.DiagnosticFile.Directory, $config.DataProtection.KeyDirectory)) {
    if ([string]::IsNullOrWhiteSpace($folder)) { throw 'Log/key directory is missing.' }
    $expandedPath = [Environment]::ExpandEnvironmentVariables($folder)
    New-Item -ItemType Directory -Path $expandedPath -Force | Out-Null
    icacls.exe $expandedPath /grant '*S-1-5-19:(OI)(CI)M'
    if ($LASTEXITCODE -ne 0) { throw 'Folder permission update failed.' }
}
```

Install the approved HTTPS certificate, including its private key, in **Local Computer > Personal**. Open the certificate manager:

```powershell
certlm.msc
```

Under **Personal > Certificates**, select the configured certificate, then **All Tasks > Manage Private Keys**. Add `LOCAL SERVICE` with **Read** permission. The certificate must match the browser hostname and be trusted on the RDS clients. Trust on the app server alone does not establish trust for staff browsers. Keep existing encryption keys; do not delete them when changing the service's key directory.

### 3a. Create and trust a self-signed HTTPS certificate

Use this when no CA-issued certificate is available. A self-signed certificate costs nothing, but every machine that browses the site must trust it explicitly. Run in Administrator PowerShell on the app server. Set `$hostName` to the exact name staff type in the browser:

```powershell
$hostName = 'timesheet.websterslawyers.com.au'
$expiry = (Get-Date).AddYears(6)
$certificate = New-SelfSignedCertificate -DnsName $hostName `
    -CertStoreLocation 'Cert:\LocalMachine\My' `
    -FriendlyName ('WL TimeSheet HTTPS ' + $expiry.Year) `
    -NotAfter $expiry
$certificate | Format-List Subject, NotAfter, Thumbprint
$cerPath = 'C:\WL-TImesheet\WLTimeSheet-HTTPS-' + $expiry.Year + '.cer'
Export-Certificate -Cert $certificate -FilePath $cerPath
```

This creates the certificate with its private key in **Local Computer > Personal** and exports the public part as a `.cer` file. The `.cer` contains no private key and is safe to copy to other machines.

Trust the certificate on the app server. With `AllowInvalid: false`, the app loads only a certificate Windows considers valid, so an untrusted self-signed certificate can prevent HTTPS from starting:

```powershell
Import-Certificate -FilePath $cerPath -CertStoreLocation 'Cert:\LocalMachine\Root'
```

The certificate must now appear in both stores in `certlm.msc`: **Personal > Certificates** (used by the app; do not move or delete it) and **Trusted Root Certification Authorities > Certificates** (trust). When using the **Install Certificate** wizard on the `.cer` file instead, choose **Local Machine**, then **Place all certificates in the following store**, and browse to **Trusted Root Certification Authorities**; the automatic store selection does not establish trust.

Then complete the configuration:

1. Grant `LOCAL SERVICE` **Read** on the private key through **Manage Private Keys**, as described in step 3.
2. In `appsettings.Production.json`, set the certificate `Subject` to the hostname and add the hostname to `AllowedHosts`.
3. Run `Restart-Service WLTimeSheet`.
4. Import the same `.cer` into **Trusted Root Certification Authorities** on every RDS host or PC that opens the site, or ask Les to distribute it by Group Policy.

When the certificate nears expiry, repeat this section. The app selects the certificate by subject, so remove the expired one from both stores after the new one is confirmed working.

### 4. Initialize a new database only

Skip this step when the schema and 38 task types are already initialized. Windows elevation alone does not provide SQL administrator access. Use the Windows account with SQL migration permissions. Stop an existing app service before maintenance:

```powershell
Set-Location 'C:\WL-TImesheet'
if (Get-Service WLTimeSheet -ErrorAction SilentlyContinue) {
    Stop-Service WLTimeSheet
}
$password = Read-Host 'Initial timesheet administrator password' -AsSecureString
$env:Bootstrap__AdminPassword = [Net.NetworkCredential]::new('', $password).Password
try {
    & '.\WL TimeSheet.exe' --environment Production --initialize --import-template
    if ($LASTEXITCODE -ne 0) { throw 'Initialization failed. Resolve the error before continuing.' }
} finally {
    Remove-Item Env:Bootstrap__AdminPassword -ErrorAction SilentlyContinue
    Remove-Variable password -ErrorAction SilentlyContinue
}
```

Expected result: migrations applied, task types imported, and `Database initialization completed`. Normal startup does not create the schema. This step does not require deleting an existing database.

### 5. Grant the service SQL access in SSMS

Connect SSMS to the same SQL instance that hosts `WLTimeSheet`. Check the connection:

```sql
SELECT @@SERVERNAME AS SqlServer,
       ORIGINAL_LOGIN() AS LoginName,
       IS_SRVROLEMEMBER('sysadmin') AS IsSqlAdmin,
       HAS_DBACCESS('WLTimeSheet') AS CanAccessTimesheet;
```

During setup, `WSLTimesheet\TimesheetAdmin` returned `1` for both checks. Use that administrator connection or another authorized SQL administrator. Run this block in SSMS only after confirming database access:

```sql
USE [master];
GO
IF NOT EXISTS (
    SELECT 1 FROM sys.server_principals
    WHERE name = N'NT AUTHORITY\LOCAL SERVICE'
)
    CREATE LOGIN [NT AUTHORITY\LOCAL SERVICE] FROM WINDOWS;
GO
USE [WLTimeSheet];
GO
IF USER_ID(N'NT AUTHORITY\LOCAL SERVICE') IS NULL
    CREATE USER [NT AUTHORITY\LOCAL SERVICE]
    FOR LOGIN [NT AUTHORITY\LOCAL SERVICE];
GO
GRANT CONNECT TO [NT AUTHORITY\LOCAL SERVICE];
GRANT SELECT, INSERT, UPDATE, DELETE
ON SCHEMA::dbo TO [NT AUTHORITY\LOCAL SERVICE];
GO
```

The app service needs these data permissions once; each employee does not need a SQL login. Do not continue if the SQL block reports errors.

### 6. Allow HTTPS from the RDS hosts

Ask Les for the RDS source IP addresses as seen by the app server. Enter those addresses below; do not substitute the public RDP gateway name or open SQL to staff. Check for an existing managed firewall rule first. If a suitable TCP 443 rule already exists, skip creation.

```powershell
Get-NetConnectionProfile
Get-NetFirewallRule -Name 'WLTimeSheet-HTTPS-443' -ErrorAction SilentlyContinue

# Run only when a new rule is needed. Enter actual RDS IPs or approved CIDR ranges.
$rdsAddresses = ((Read-Host 'Allowed RDS source IPs/CIDRs, comma-separated') -split ',') |
    ForEach-Object { $_.Trim() } | Where-Object { $_ }
if (-not $rdsAddresses) { throw 'RDS source addresses are required.' }
New-NetFirewallRule -Name 'WLTimeSheet-HTTPS-443' -DisplayName 'WL TimeSheet HTTPS 443' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 443 -RemoteAddress $rdsAddresses -Profile Domain,Private
```

Have Les confirm the applicable network profile and any centrally managed rules. Do not switch profiles simply to bypass policy. Retire the old app-specific 7294 rule only after port 443 access succeeds.

### 7. Install once, then start or restart

Close any manually running timesheet EXE first. For a **new service only**, run in Administrator PowerShell:

```powershell
Set-Location 'C:\WL-TImesheet'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '.\Service.ps1' -Action Install
```

Installation starts the service automatically. The execution-policy option applies to this child process; organization policy may still require a signed script. For the **already installed service**, use this instead:

```powershell
Restart-Service WLTimeSheet
```

Check the identity, executable path and listener:

```powershell
Get-Service WLTimeSheet
Get-CimInstance Win32_Service -Filter "Name='WLTimeSheet'" |
    Format-List Name, State, StartName, PathName, ExitCode
Get-NetTCPConnection -LocalPort 443 -State Listen |
    Select-Object LocalAddress, LocalPort, OwningProcess
```

Expected: service `Running`, identity `NT AUTHORITY\LocalService`, the EXE in `C:\WL-TImesheet`, and a listener on port `443`.

### 8. Open and verify the application

From the staff RDS session, test the actual configured hostname:

```powershell
Test-NetConnection WSLTimesheet -Port 443
curl.exe -v --negotiate -u : --noproxy "*" 'https://WSLTimesheet/'
Start-Process 'https://WSLTimesheet/'
```

The curl command uses the current Windows identity and validates the certificate. An initial `401` challenge can be part of negotiation; a final `401` means authentication has not succeeded. A browser login prompt uses the employee's Windows credentials, not the application's administration password.

#### Sign-in prompt repeats when browsing on the app server

If the browser on the app server keeps showing **Sign in to access this site** after correct Windows credentials, Windows is rejecting local sign-in to a hostname that is not the computer's own name (the loopback check). Staff on other machines are not affected. Confirm it on the app server directly after a failed prompt:

```powershell
Get-WinEvent -FilterHashtable @{LogName='System'; Id=6037; StartTime=(Get-Date).AddHours(-2)} -MaxEvents 3 |
    Format-List TimeCreated, Message
```

An event naming `WL TimeSheet.exe` and `HTTP/<hostname>` with "The target name used is not valid" confirms the cause. Register the hostname as a local name. This is a registry change; agree it with Les first:

```powershell
New-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Lsa\MSV1_0' `
    -Name BackConnectionHostNames -PropertyType MultiString `
    -Value 'timesheet.websterslawyers.com.au'
Restart-Service WLTimeSheet
```

If the command reports that `BackConnectionHostNames` already exists, do not overwrite it; add the hostname to the existing list instead. Close the browser completely and reopen the site. If the prompt still repeats and a new 6037 event is logged, reboot the server during a maintenance window. Do not disable the loopback check globally with `DisableLoopbackCheck`.

Verify that the page loads, an entry saves and reloads, and Excel export succeeds. Test a second employee account for separate ownership. A running service or successful TCP check alone is not sufficient.

If the page shows a reference-number error, read the latest log on the app server:

```powershell
$latestLog = Get-ChildItem 'C:\ProgramData\WLTimeSheet\Logs' -File -Filter '*.log' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if ($latestLog) {
    Get-Content -LiteralPath $latestLog.FullName -Tail 150
} else {
    Write-Output 'No .log file found. Check the configured log directory and Windows Event Viewer.'
}
```

TLS failure: check the certificate and LocalService private-key access. SQL login failure: check the service identity and the SSMS grants. Key-decryption failure: check the account-specific key directory. Preserve the actual exception and request reference when reporting a failure.

## Configure the server

1. Copy this entire folder to a permanent location, for example `C:\Apps\WLTimeSheet`.
2. Edit `appsettings.Production.json`: set the SQL connection, allowed DNS hostnames, HTTPS port and certificate subject. Install a valid HTTPS certificate with its private key in Local Computer > Personal. Give the service account read access to that private key. Use a trusted SQL Server certificate matching the SQL hostname.
3. Select the service identity. By default the script uses LocalService. For Windows integrated SQL authentication on another server, use a dedicated domain service account with SQL access and the **Log on as a service** right. Grant only the database permissions the application needs. Database initialization needs migration/schema permissions; ordinary operation does not. A custom domain account/hostname may also need an HTTP SPN configured by your domain administrator for Kerberos.
4. Allow the configured HTTPS port through the server firewall for the intended clients. Configure client browsers to permit Windows integrated authentication for this intranet hostname.

The app uses Windows authentication and HTTPS. It does not use the developer's user secrets, development certificate or sample review database. Secure administrator cookies require HTTPS.

## Initialize a new database

Skip this for an existing, initialized database. Run the EXE interactively under an identity with the required SQL migration permissions. In PowerShell, from this folder:

```powershell
$password = Read-Host 'Initial administrator password' -AsSecureString
$env:Bootstrap__AdminPassword = [Net.NetworkCredential]::new('', $password).Password
try {
    & '.\WL TimeSheet.exe' --environment Production --initialize --import-template
    if ($LASTEXITCODE -ne 0) { throw 'Database initialization failed.' }
} finally {
    Remove-Item Env:Bootstrap__AdminPassword -ErrorAction SilentlyContinue
}
```

Initialization explicitly applies database migrations and imports the embedded task workbook. Normal service startup never changes the database schema. Do not put the bootstrap password in the configuration files.

## Install and run as a service

Open PowerShell **as Administrator** in this folder after configuring SQL and HTTPS:

```powershell
# LocalService identity (suitable only when granted the required SQL access):
.\Service.ps1 -Action Install

# Alternatively, install using a dedicated domain service account:
.\Service.ps1 -Action Install -Credential (Get-Credential 'DOMAIN\svc-timesheet')
```

Choose one installation command. The script grants the identity read/execute access to this folder and modify access to the configured log/key folders, registers automatic startup and crash recovery, and starts the service. It does not create SQL logins, install certificates, grant certificate access, configure SPNs, or open firewall ports. Keep deployment files writable only by administrators.

If local PowerShell policy blocks scripts, use `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Service.ps1 -Action Install` from the elevated terminal. This applies only to that PowerShell process; organizational policy may require a signed script instead.

```powershell
.\Service.ps1 -Action Status
.\Service.ps1 -Action Stop
.\Service.ps1 -Action Start
.\Service.ps1 -Action Restart
.\Service.ps1 -Action Uninstall
```

To diagnose startup interactively, stop the service and run `& '.\WL TimeSheet.exe' --environment Production`. Use Ctrl+C to stop. Inspect `%ProgramData%\WLTimeSheet\Logs` and Windows Event Viewer if startup fails. A Running service status alone does not verify SQL connectivity; open the site and check the timesheet and Excel export. Windows-protected encryption keys belong to the configured account: changing accounts can invalidate existing sessions.

## Update

### Administrator-configurable units

After this release is installed, unlock **Administration**, open **Timesheet settings**, and set **Minutes per unit** to a whole number from **1 to 60**. Examples: `1` = one minute, `6` = six minutes, `15` = fifteen minutes. Click **Save settings**. No code change, publish or service restart is needed for later setting changes.

The setting applies when a daily timesheet is first created, including a newly created timesheet for an earlier date. Existing daily timesheets retain their saved rate, even when tasks are added or edited later. A day created at six minutes per unit keeps that rate after the default changes to one. Clock-in remains at the entered minute. The workspace shows the day's rate, and exports identify the rate used. Duration totals remain comparable when unit rates differ; raw unit counts may represent different amounts of time.

**One deployment upgrade is required to add this feature.** Back up the database, stop the service, copy the updated EXE while preserving server settings, then run `--environment Production --initialize` under the SQL migration administrator as shown below. This applies `20261001062615_ConfigurableUnits` and any preceding pending migrations. Existing minute-based records and the initial administrator default are assigned a rate of `1`, preserving durations. An older six-minute deployment first runs the preceding MinuteUnits conversion exactly once. Do not change the default by editing the database directly.

After initialization succeeds, start the service and reopen browser tabs. Verify saving a new default, creating a new daily timesheet with that rate, and viewing an older day with its original rate. Settings changes use row-version checks to avoid overwriting another administrator's changes. Rollback must restore the matching EXE and database backup; the migration refuses to remove saved custom rates.

### Required upgrade: root address and one-minute units (1 October 2026)

The section below describes the preceding release's migration and the upgrade commands, which also apply to the configurable-unit release above.

The timesheet now opens directly at the site root, for example `https://timesheet.websterslawyers.com.au/`. Old `/timesheet` bookmarks redirect to `/`. Use the DNS name and certificate actually configured on the server; publication does not configure DNS or certificates.

Each unit now means **one minute**. New/saved daily clock-in details keep the entered minute. Migration leaves historical stored starts unchanged. The obsolete `Timesheet:RoundStartDown` setting is ignored and can be removed.

**Apply the database migration before starting this EXE.** `20261001060914_MinuteUnits` multiplies existing entry units by six exactly once, preserving durations and totals: 10 old units become 60 new units, still one hour. It also updates the unit limit to 1440 and the SQL reporting view. Do not manually multiply units or use the old EXE against the upgraded database. The new EXE refuses startup when migrations are pending.

On the app server, stop the service in Administrator PowerShell:

```powershell
Stop-Service WLTimeSheet
```

Have the SQL administrator take and verify a database backup in SSMS. Back up the existing EXE and configurations outside the deployment folder. Copy the new `WL TimeSheet.exe` into `C:\WL-TImesheet`, preserving existing SQL/certificate settings and persistent encryption keys.

Run this from a Windows session with SQL migration permissions (the working TimesheetAdmin SQL account during setup). Windows elevation alone and LocalService's ordinary data permissions are insufficient:

```powershell
Set-Location 'C:\WL-TImesheet'
& '.\WL TimeSheet.exe' --environment Production --initialize
if ($LASTEXITCODE -ne 0) { throw 'Upgrade failed. Keep the service stopped and review the log.' }
```

An initialized database retains the existing administration password; no bootstrap password or template reimport is needed. Only after initialization succeeds:

```powershell
Start-Service WLTimeSheet
Get-Service WLTimeSheet
```

Close old browser tabs and reopen the root address to load the new JavaScript. Verify that 1 unit adds one minute, historical durations are unchanged, and calendar totals and Excel/CSV exports match. Rollback requires the old EXE and pre-upgrade database backup together. A down migration refuses entries not divisible by six rather than discarding minutes.

### Move an existing installation from HTTPS port 7294 to 443

Port 443 is the production template default. On the app server, check for an existing listener before making the change:

```powershell
Get-NetTCPConnection -LocalPort 443 -State Listen -ErrorAction SilentlyContinue |
    Select-Object LocalAddress, LocalPort, OwningProcess
```

If a listener is returned, identify its owner and coordinate the binding with the server administrator before continuing. Do not stop another application's listener.

Back up the deployed `appsettings.Production.json`, then change only `Kestrel.Endpoints.Https.Url` to `https://0.0.0.0:443`. Preserve the server's SQL, certificate, hostname and encryption-key settings; do not replace them with the placeholder template. Arrange an inbound TCP 443 firewall rule scoped to the intended RDS hosts.

Restart and verify from Administrator PowerShell:

```powershell
Restart-Service WLTimeSheet
Get-NetTCPConnection -LocalPort 443 -State Listen
```

Open `https://WSLTimesheet/` using the configured hostname and verify authenticated access from an RDS session. No EXE rebuild or service reinstallation is required for this configuration change. The certificate remains applicable when the hostname stays the same. After successful validation, retire the old 7294 firewall rule if it was dedicated to this app. For rollback, restore the backed-up configuration and restart the service.

Stop the service, back up the two configuration files, replace the EXE with the new published EXE, retain your server configuration, apply any explicitly required database migration, then start the service. Do not delete the persistent encryption keys. Uninstalling the service leaves files and data intact.

## Build this package from source

Run these commands on the **development computer**, where the source code and **.NET 10 SDK** are installed. The destination Windows x64 server does not need the SDK or a separate .NET runtime installation.

### 1. Open the project directory

Use PowerShell or the Visual Studio terminal. This is the inner project folder containing the `.csproj`, not just the solution folder. Adjust the path if the repository was copied elsewhere:

```powershell
Set-Location 'C:\Users\patrickb\source\repos\WL TimeSheet\WL TimeSheet'
dotnet --list-sdks
Get-Item '.\WL TimeSheet.csproj'
Get-Item '.\Properties\PublishProfiles\WindowsService.pubxml'
```

Confirm a `10.0.x` SDK is listed. Restore/publish may need network access to download NuGet packages and the Windows runtime pack.

### 2. Publish using the existing profile

Back up any customized files in `artifacts\publish-service` first: publishing copies the source configuration templates into that folder. Do not publish directly over a running production installation.

```powershell
dotnet publish '.\WL TimeSheet.csproj' -c Release -p:PublishProfile=WindowsService
if ($LASTEXITCODE -ne 0) { throw 'Publish failed. Review the errors above.' }
```

This command restores dependencies, builds Release and writes the package to `artifacts\publish-service`. Use this project's profile because it also controls bundled assets, external configuration and the `BUNDLED_DEPLOYMENT` compilation setting.

For a fresh output directory that preserves the existing package, use this **alternative** command instead:

```powershell
$publishFolder = Join-Path $PWD ('artifacts\publish-service-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
dotnet publish '.\WL TimeSheet.csproj' -c Release -p:PublishProfile=WindowsService -o $publishFolder
if ($LASTEXITCODE -ne 0) { throw 'Publish failed. Review the errors above.' }
Get-ChildItem -LiteralPath $publishFolder
```

The profile already sets the following; no project edits are required:

| Setting | Value and purpose |
| --- | --- |
| `RuntimeIdentifier` | `win-x64`: targets 64-bit Windows |
| `SelfContained` | `true`: includes the .NET runtime |
| `PublishSingleFile` | `true`: bundles the application into one EXE |
| `IncludeAllContentForSelfExtract` | `true`: bundles content for extraction at startup |
| `EnableCompressionInSingleFile` | `true`: compresses the bundle |
| `PublishTrimmed` | `false`: preserves code required by MVC, EF Core and Excel functionality |
| `DebugType` / `DebugSymbols` | `none` / `false`: omits separate debugging symbols |

This app currently relies on all-content extraction for its packaged assets and content-root handling. Microsoft describes this extraction mode as a compatibility mode; changing it requires application changes and runtime verification. See [single-file deployment](https://learn.microsoft.com/dotnet/core/deploying/single-file/overview).

### 3. Check the published files

For the standard output directory:

```powershell
$publishFolder = Join-Path $PWD 'artifacts\publish-service'
Get-ChildItem -LiteralPath $publishFolder | Select-Object Name, Length
$expectedFiles = @('WL TimeSheet.exe', 'appsettings.json', 'appsettings.Production.json', 'README.md', 'Service.ps1')
$actualItems = @(Get-ChildItem -LiteralPath $publishFolder -Force)
$differences = Compare-Object -ReferenceObject $expectedFiles -DifferenceObject @($actualItems.Name)
if ($differences -or ($actualItems | Where-Object { $_.PSIsContainer })) {
    $differences
    throw 'Unexpected package contents. Check the publish output or publish into a fresh directory.'
}
Write-Output 'Package contains the expected five files.'
```

If you used the alternative timestamped output, retain its `$publishFolder` value instead of assigning the standard path above. Expected output:

```text
WL TimeSheet.exe
appsettings.json
appsettings.Production.json
README.md
Service.ps1
```

There is one executable; the two configurations remain editable beside it, and the README and service script are intentional supporting files. The SQL database, HTTPS certificate, logs and persistent encryption keys are not packaged inside the EXE. A file-list check confirms package layout, not application health: complete the browser/save/export checks after deployment.

### 4. Copy or zip the package for deployment

Copy the five files from the chosen output folder to the server. Optionally create a new ZIP on the development computer:

```powershell
$archivePath = Join-Path $PWD ('artifacts\WLTimeSheet-win-x64-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.zip')
Compress-Archive -Path (Join-Path $publishFolder '*') -DestinationPath $archivePath
Get-Item -LiteralPath $archivePath
```

Publishing does not automatically refresh an existing ZIP. For a new server, follow the installation walkthrough above. For an existing service, stop it, retain the actual server configuration, replace the EXE, apply any explicitly required schema migration using the deployment administrator, and restart. Do not overwrite working SQL/certificate settings with the package's placeholders.

Existing SQL client managed-networking configuration is retained; see the source repository's `docs/THIRD_PARTY_LIBRARIES.md` for its deployment support limitation.

Reference: [Microsoft Windows Service hosting](https://learn.microsoft.com/aspnet/core/host-and-deploy/windows-service?view=aspnetcore-10.0) and [single-file deployment](https://learn.microsoft.com/dotnet/core/deploying/single-file/overview).
