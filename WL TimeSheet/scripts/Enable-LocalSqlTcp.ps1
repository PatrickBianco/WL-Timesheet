# Run in an elevated PowerShell. Enables ONLY loopback TCP on the inspected SQL 2022 default instance.
# Restarting the service briefly interrupts connections to this SQL instance.
$ErrorActionPreference = 'Stop'
$tcpPath = 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQLServer\SuperSocketNetLib\Tcp'
if (-not (Test-Path -LiteralPath $tcpPath)) { throw 'Expected SQL Server 2022 default instance not found.' }
$settings = Get-ItemProperty -LiteralPath $tcpPath
if ($settings.Enabled -ne 0) { throw 'TCP is already enabled; inspect the existing configuration instead of replacing it.' }
$ipKeys = Get-ChildItem -LiteralPath $tcpPath | Where-Object { $_.PSChildName -match '^IP\d+$' }
$loopback = $ipKeys | Where-Object { (Get-ItemProperty -LiteralPath $_.PSPath).IpAddress -eq '127.0.0.1' }
if (@($loopback).Count -ne 1) { throw 'Expected a single IPv4 loopback entry.' }
$backup = @($settings) + @($ipKeys | ForEach-Object { Get-ItemProperty -LiteralPath $_.PSPath })
$backup | Export-Clixml -LiteralPath (Join-Path $PSScriptRoot 'sql-tcp-before.xml')
foreach ($ipKey in $ipKeys) { Set-ItemProperty -LiteralPath $ipKey.PSPath -Name Enabled -Value 0 }
Set-ItemProperty -LiteralPath $loopback.PSPath -Name TcpPort -Value '1433'
Set-ItemProperty -LiteralPath $loopback.PSPath -Name TcpDynamicPorts -Value ''
Set-ItemProperty -LiteralPath $loopback.PSPath -Name Enabled -Value 1
Set-ItemProperty -LiteralPath $tcpPath -Name ListenOnAllIPs -Value 0
Set-ItemProperty -LiteralPath $tcpPath -Name Enabled -Value 1
Restart-Service -Name MSSQLSERVER
Write-Output 'SQL Server TCP enabled on 127.0.0.1:1433 only. Original registry values saved in scripts/sql-tcp-before.xml.'
