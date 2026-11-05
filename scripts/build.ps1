[CmdletBinding()]
param(
    [switch]$Installer,
    [string]$NsisCompiler = ""
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'Install the .NET 10 SDK to build from source. End users do not need the SDK.'
    }
    function Invoke-Dotnet {
        & dotnet @args
        if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE." }
    }

    $app = 'src/WindowsSystemMonitor/WindowsSystemMonitor.csproj'
    $tests = 'tests/WindowsSystemMonitor.Tests/WindowsSystemMonitor.Tests.csproj'
    $processTests = 'tests/WindowsSystemMonitor.ProcessTests/WindowsSystemMonitor.ProcessTests.csproj'
    # Remove only this script's generated release products, so a failed or portable-only
    # build cannot leave an old installer looking like part of the current release.
    foreach ($name in @('WindowsSystemMonitor-Portable-win-x64.zip', 'WindowsSystemMonitor-Setup.exe', 'SHA256SUMS.txt')) {
        $oldProduct = Join-Path $root "dist/$name"
        if (Test-Path -LiteralPath $oldProduct) { Remove-Item -LiteralPath $oldProduct -Force }
    }
    Invoke-Dotnet restore $app
    Invoke-Dotnet restore $tests
    Invoke-Dotnet restore $processTests
    Invoke-Dotnet build $app -c Release --no-restore
    Invoke-Dotnet run --project $tests -c Release --no-restore
    Invoke-Dotnet run --project $processTests -c Release --no-restore

    $portable = Join-Path $root 'dist/portable'
    if (Test-Path $portable) { Remove-Item -LiteralPath $portable -Recurse -Force }
    New-Item -ItemType Directory -Path $portable -Force | Out-Null
    Invoke-Dotnet publish $app -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false `
        -p:DebugType=None -p:DebugSymbols=false -o $portable
    $exe = Join-Path $portable 'WindowsSystemMonitor.exe'
    if (-not (Test-Path $exe)) { throw 'Publish did not produce WindowsSystemMonitor.exe.' }
    Copy-Item -LiteralPath 'docs/portable-readme.txt' -Destination $portable

    if ($env:OS -eq 'Windows_NT') {
        & "$PSScriptRoot/smoke-test.ps1" -Executable $exe
    }
    $archive = Join-Path $root 'dist/WindowsSystemMonitor-Portable-win-x64.zip'
    Compress-Archive -Path "$portable/*" -DestinationPath $archive -Force
    $releaseArtifacts = @($archive)

    if ($Installer) {
        if (-not $NsisCompiler) {
            $compilerCommand = Get-Command makensis -ErrorAction SilentlyContinue
            if ($compilerCommand) { $NsisCompiler = $compilerCommand.Source }
            elseif (Test-Path "${env:ProgramFiles(x86)}\NSIS\makensis.exe") {
                $NsisCompiler = "${env:ProgramFiles(x86)}\NSIS\makensis.exe"
            }
        }
        if (-not $NsisCompiler -or -not (Test-Path -LiteralPath $NsisCompiler)) {
            throw 'Install NSIS 3.11 or newer from https://nsis.sourceforge.io/ or pass -NsisCompiler with the path to makensis.'
        }
        $compilerPrefix = if ($env:OS -eq 'Windows_NT') { '/' } else { '-' }
        & $NsisCompiler "${compilerPrefix}WX" "${compilerPrefix}DAPP_VERSION=1.0.0" "$root/installer/WindowsSystemMonitor.nsi"
        if ($LASTEXITCODE -ne 0) { throw "NSIS failed with exit code $LASTEXITCODE." }
        if (-not (Test-Path "$root/dist/WindowsSystemMonitor-Setup.exe")) {
            throw 'The installer compiler did not produce the expected installer.'
        }
        $releaseArtifacts += "$root/dist/WindowsSystemMonitor-Setup.exe"
    }

    $releaseArtifacts | ForEach-Object {
        $hash = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $(Split-Path -Leaf $_)"
    } | Set-Content -LiteralPath "$root/dist/SHA256SUMS.txt" -Encoding ascii
    Write-Host "Release outputs: $root/dist"
} finally {
    Pop-Location
}
