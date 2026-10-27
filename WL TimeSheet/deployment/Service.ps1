#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [ValidateSet('Install', 'Start', 'Stop', 'Restart', 'Status', 'Uninstall')]
    [string]$Action = 'Status',
    [PSCredential]$Credential
)

$ErrorActionPreference = 'Stop'
$serviceName = 'WLTimeSheet'
$executable = Join-Path $PSScriptRoot 'WL TimeSheet.exe'

switch ($Action) {
    'Install' {
        if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
            throw 'WLTimeSheet already exists. Stop it before replacing published files, then use -Action Start.'
        }
        if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
            throw "Executable not found: $executable"
        }
        $settings = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'appsettings.Production.json') -Raw | ConvertFrom-Json
        if ($settings.ConnectionStrings.Timesheet -match 'YOUR-SQL-SERVER' -or $settings.Kestrel.Endpoints.Https.Certificate.Subject -eq 'timesheet.example.local') {
            throw 'Configure SQL Server, AllowedHosts and the HTTPS certificate in appsettings.Production.json first.'
        }

        $identity = if ($Credential) { $Credential.UserName } else { 'NT AUTHORITY\LocalService' }
        $account = New-Object System.Security.Principal.NTAccount($identity)
        $sid = $account.Translate([System.Security.Principal.SecurityIdentifier])
        # Runtime data lives outside the deployment folder; only this identity gets added access.
        $dataFolders = @($settings.Logging.DiagnosticFile.Directory, $settings.DataProtection.KeyDirectory)
        foreach ($folder in $dataFolders) {
            if ([string]::IsNullOrWhiteSpace($folder)) { throw 'Configure the log and data-protection directories first.' }
            $dataPath = [Environment]::ExpandEnvironmentVariables($folder)
            New-Item -ItemType Directory -Path $dataPath -Force | Out-Null
            $acl = Get-Acl -LiteralPath $dataPath
            $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($sid, 'Modify', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
            $acl.AddAccessRule($rule)
            Set-Acl -LiteralPath $dataPath -AclObject $acl
        }

        # Grant read/execute on deployment files, not write access to the EXE/configuration.
        $acl = Get-Acl -LiteralPath $PSScriptRoot
        $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($sid, 'ReadAndExecute', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
        $acl.AddAccessRule($rule)
        Set-Acl -LiteralPath $PSScriptRoot -AclObject $acl

        $parameters = @{
            Name = $serviceName
            DisplayName = 'WL TimeSheet'
            Description = 'Websters Lawyers timesheet web application'
            BinaryPathName = ('"{0}" --environment Production' -f $executable)
            StartupType = 'Automatic'
        }
        if ($Credential) {
            $parameters.Credential = $Credential
        } else {
            $parameters.Credential = New-Object PSCredential($identity, (New-Object System.Security.SecureString))
        }
        New-Service @parameters | Out-Null
        & sc.exe failure $serviceName reset= 86400 actions= restart/60000/restart/60000/restart/60000
        if ($LASTEXITCODE -ne 0) { throw 'Service installed, but configuring automatic recovery failed.' }
        Start-Service -Name $serviceName
        (Get-Service -Name $serviceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
        Get-Service -Name $serviceName
    }
    'Start' { Start-Service -Name $serviceName; Get-Service -Name $serviceName }
    'Stop' { Stop-Service -Name $serviceName; Get-Service -Name $serviceName }
    'Restart' { Restart-Service -Name $serviceName; Get-Service -Name $serviceName }
    'Status' { Get-Service -Name $serviceName }
    'Uninstall' {
        $service = Get-Service -Name $serviceName -ErrorAction Stop
        if ($service.Status -ne 'Stopped') { Stop-Service -Name $serviceName }
        & sc.exe delete $serviceName
        if ($LASTEXITCODE -ne 0) { throw 'Unable to delete the service.' }
        Write-Output 'Service removed. Deployment files, database, logs and encryption keys were retained.'
    }
}
