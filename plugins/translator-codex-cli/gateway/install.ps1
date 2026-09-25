$ErrorActionPreference = 'Stop'
$bundleSource = $PSScriptRoot
$registration = Get-Content -LiteralPath (Join-Path $bundleSource 'registration.json') -Raw | ConvertFrom-Json
if (([Uri]$registration.server_url).Scheme -ne 'https') { throw 'HTTPS registration is required.' }
$dataDirectory = Join-Path $env:LOCALAPPDATA 'TransSysGateway'
$installDirectory = Join-Path $dataDirectory 'app'
$devicePath = Join-Path $dataDirectory 'device.json'
if (Test-Path -LiteralPath $devicePath) {
    $existingDevice = Get-Content -LiteralPath $devicePath -Raw | ConvertFrom-Json
    if ($existingDevice.client_instance_id -ne $registration.client_instance_id -or $existingDevice.server_url -ne $registration.server_url) {
        throw 'Existing gateway belongs to another client instance. Revoke and uninstall it before switching.'
    }
}
New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
$identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
& icacls.exe $dataDirectory '/inheritance:r' '/grant:r' "${identity}:(OI)(CI)F" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Unable to restrict gateway credential directory.' }
Get-ChildItem -LiteralPath $bundleSource | Copy-Item -Destination $installDirectory -Recurse -Force
$nodePath = Join-Path $installDirectory 'node\node.exe'
$entryPath = Join-Path $installDirectory 'dist\main.js'
$runner = Join-Path $installDirectory 'run-hidden.ps1'
@'
$ErrorActionPreference = 'Stop'
$nodePath = Join-Path $PSScriptRoot 'node\node.exe'
$entryPath = Join-Path $PSScriptRoot 'dist\main.js'
Start-Process -FilePath $nodePath -ArgumentList ('"' + $entryPath + '"') -WindowStyle Hidden -WorkingDirectory $PSScriptRoot
'@ | Set-Content -LiteralPath $runner -Encoding UTF8
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument ('-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + $runner + '"')
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $identity
$principal = New-ScheduledTaskPrincipal -UserId $identity -LogonType Interactive -RunLevel Limited
Register-ScheduledTask -TaskName 'TransSysGateway' -Action $action -Trigger $trigger -Principal $principal -Force | Out-Null
Start-ScheduledTask -TaskName 'TransSysGateway'
Write-Host 'Gateway installed. It connects automatically under your Windows account.'
Write-Host 'If the app reports login_required, run login.ps1 from this folder once.'
