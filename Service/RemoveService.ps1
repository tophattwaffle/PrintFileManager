$ServiceName = "PrintFileManager"
$CurrentDir = $PSScriptRoot
$NssmPath   = Join-Path -Path $CurrentDir -ChildPath "nssm.exe"

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    
    Write-Host "Stopping service '$ServiceName'..." -ForegroundColor Cyan
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue

    if (Test-Path $NssmPath) {
        & $NssmPath remove $ServiceName confirm
        Write-Host "Service '$ServiceName' removed successfully." -ForegroundColor Green
    } else {
        Write-Error "nssm.exe not found. Cannot remove service via NSSM."
    }

} else {
    Write-Warning "Service '$ServiceName' is not installed."
}