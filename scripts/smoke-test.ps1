[CmdletBinding()]
param([Parameter(Mandatory)][string]$Executable)

$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'The UI smoke test requires Windows.' }
$exePath = (Resolve-Path -LiteralPath $Executable).Path
$process = Start-Process -FilePath $exePath -ArgumentList '--smoke-test' -PassThru
try {
    if (-not $process.WaitForExit(30000)) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        throw 'UI smoke test timed out after 30 seconds.'
    }
    if ($process.ExitCode -ne 0) { throw "UI smoke test failed with exit code $($process.ExitCode)." }
    # Compare the image path to avoid disrupting an unrelated development instance.
    $remaining = @(Get-Process -Name WindowsSystemMonitor -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -eq $exePath } catch { $false }
    })
    if ($remaining.Count -gt 0) { throw 'An application process remains after the smoke test closed.' }
    Write-Host 'PASS: published application opened, completed its self-check, and exited.'
} finally {
    $process.Dispose()
}
