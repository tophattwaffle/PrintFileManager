$ServiceName = "PrintFileManager"
$DisplayName = "Printer File Manager"
$Description = "Service for sending gcode files to printers automagically"

$CurrentDir  = $PSScriptRoot
$NssmPath    = Join-Path -Path $CurrentDir -ChildPath "nssm.exe"
$BinaryPath  = Join-Path -Path $CurrentDir -ChildPath "PrintFileManager.exe"
$ErrorLogPath = Join-Path -Path $CurrentDir -ChildPath "pfm_stderr.log"

if (-not (Test-Path $NssmPath)) { throw "nssm.exe not found in $CurrentDir" }
if (-not (Test-Path $BinaryPath)) { throw "pPrintFileManagerfm.exe not found in $CurrentDir" }

& $NssmPath install $ServiceName "$BinaryPath"

& $NssmPath set $ServiceName AppStderr "$ErrorLogPath"
& $NssmPath set $ServiceName AppDirectory "$CurrentDir"

& $NssmPath set $ServiceName DisplayName "$DisplayName"
& $NssmPath set $ServiceName Description "$Description"

& $NssmPath set $ServiceName Start SERVICE_AUTO_START

Start-Service -Name $ServiceName

Write-Host "Service '$ServiceName' installed and started successfully using NSSM." -ForegroundColor Green