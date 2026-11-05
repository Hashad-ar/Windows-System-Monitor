# Windows System Monitor

A compact, dark Windows desktop utility for local system monitoring and deliberate maintenance. Built with C# and .NET 10 Windows Forms. Native Windows APIs provide monitoring without WMI polling; built-in Windows tools handle privileged maintenance. No third-party runtime libraries, accounts, telemetry, services, scheduled tasks, tray process, or automatic startup.

## Download the preview

- [Windows installer](https://github.com/Hashad-ar/Windows-System-Monitor/raw/refs/heads/downloads-v1.0.0-preview.1/WindowsSystemMonitor-Setup.exe)
- [Portable ZIP](https://github.com/Hashad-ar/Windows-System-Monitor/raw/refs/heads/downloads-v1.0.0-preview.1/WindowsSystemMonitor-Portable-win-x64.zip)
- [Standalone executable](https://github.com/Hashad-ar/Windows-System-Monitor/raw/refs/heads/downloads-v1.0.0-preview.1/portable/WindowsSystemMonitor.exe)
- [Checksums and package notes](https://github.com/Hashad-ar/Windows-System-Monitor/tree/downloads-v1.0.0-preview.1)

These are unsigned preview packages. Windows runtime validation is still pending; see [the validation record](docs/validation-results.md). Packages are stored on a download branch because GitHub's release API was unavailable in the upload environment.

## Install and launch

Requires 64-bit Windows 10 22H2 or Windows 11. Use a currently serviced Windows release; application compatibility does not extend the operating system's support lifecycle.

The release targets x64. The installer also accepts systems that support x64 emulation, such as Windows 11 on ARM; ARM emulation has not been validated.

- **Installer:** run `dist/WindowsSystemMonitor-Setup.exe`. It installs for the current user and creates desktop and Start Menu shortcuts. Double-click **Windows System Monitor** to launch.
- **Portable:** extract `dist/WindowsSystemMonitor-Portable-win-x64.zip` and run `WindowsSystemMonitor.exe`. The executable is also at `dist/portable/WindowsSystemMonitor.exe` after publishing.

Release builds contain the .NET runtime; end users do not need Visual Studio, Python, Node.js, or a separate .NET installation. Single-file publishing extracts native runtime files into the user's temporary `.net` cache on first launch. The cache contains files, not a background service.

These paths describe build output; consult [validation results](docs/validation-results.md) for which artifacts were actually produced and tested. The Windows CI workflow uploads the portable ZIP, installer, and SHA-256 checksums under its **WindowsSystemMonitor-win-x64** artifact after a successful run.

Builds are unsigned unless explicitly signed by the distributor. Windows may display an unknown-publisher or SmartScreen warning. Verify the source and checksum; do not disable security protections. SHA-256 checksums detect file changes but do not authenticate a publisher.

## Everyday use

The overview shows CPU utilization, memory, system-drive capacity, network availability, uptime, and basic device details. Monitoring refreshes approximately every four seconds and pauses while minimized. Network availability means a usable network interface is present, not proof of Internet access. Missing readings appear unavailable.

| Action | Behavior and permissions |
| --- | --- |
| Clean Temporary Files | Scans `%LOCALAPPDATA%\Temp` for `.tmp` and `.temp` files older than 24 hours, shows a preview, then requires confirmation. Rechecks each candidate before deletion and skips changed, locked, protected, or linked files. Does not delete directories. No administrator permission needed. |
| Optimize Drives | Opens Windows Optimize Drives. Windows selects the appropriate treatment for SSDs and HDDs. Windows controls permissions, progress, and scheduling. |
| Check for Updates | Opens Windows Update or optional updates, or runs a real read-only `winget` check when available. Windows and winget report their own results. Application updates require selection and confirmation. |
| System Health Scan | Explicitly launches DISM ScanHealth and SFC verification with UAC. No automatic repair. Read the Windows command output for findings; a completed command is not a blanket clean-health guarantee. |
| Advanced Repair | Separately confirmed DISM RestoreHealth / SFC repair. Requires administrator approval; may modify Windows components and take significant time. No automatic restart. |
| More Tools | Opens Storage Settings, Startup Apps, Windows Security, and System Information using Windows' own interfaces. Defender scanning remains a user choice in Windows Security. |

Windows Storage Settings provides system-level cleanup including Windows temporary files. The application's own cleaner intentionally uses only stale `.tmp` and `.temp` files under `%LOCALAPPDATA%\Temp`; custom `TEMP`/`TMP` overrides are not followed. A scan considers at most 50,000 entries and reports a partial preview if it reaches that limit. The preview estimates logical file sizes; the result reports bytes of files actually deleted. This is not a guarantee of the same increase in physical free space: compression, sparse files, and concurrent disk activity can affect that measurement. Hard-linked files are skipped.

Cleanup excludes Prefetch, browser profiles/cookies, print spool, user libraries, Windows Update databases, system logs, drivers, restore points, and recovery partitions. It never executes the obsolete batch script that motivated this project. An explanation is available in the cleanup interface. There is no Recycle Bin purge.

Winget is optional and supplied by Microsoft's App Installer. It does not cover every installed program, driver, or firmware. Update results can be localized, so the application preserves native output rather than guessing counts from a translated table. On a managed PC, Windows policies still apply. Monitoring and cleanup need no network connection; update checks and some Windows repairs may contact Microsoft's configured services.

## Close and uninstall

Use the title-bar **X**, **Close**, or **Alt+F4**. Monitoring and app-owned work stop, resources are disposed, and the application exits. No application process should remain after normal monitoring closes, so the closed application consumes no CPU or RAM. There is no automatic restart.

Windows Settings, Optimize Drives, and explicitly launched servicing/update operations are separate Windows-owned tasks. Closing this application does not claim to cancel those tasks. When an operation is active, the interface explains whether you can keep waiting or leave the Windows tool running. Do not forcibly interrupt Windows servicing merely to close the monitor.

For an installed copy, use **Settings → Apps → Installed apps → Windows System Monitor → Uninstall** (Windows 10: **Apps & features**). Uninstallation removes the program and its shortcuts. For a portable copy, close it and remove its folder. A small operation-status log is retained at `%LOCALAPPDATA%\WindowsSystemMonitor\Logs\operations.log`; it records timestamps, utility names, and exit codes without command output or device details. You may delete that folder after closing the app. Local logs and runtime extraction caches are ordinary per-user files, not running components.

## Build from source

Install the **.NET 10 SDK**. To build an installer, also install **NSIS 3.11 or newer** from its official site, <https://nsis.sourceforge.io/>. No NuGet application libraries are used; restore downloads Microsoft's targeting/runtime packs as needed.

From this repository in PowerShell on Windows:

```powershell
./scripts/build.ps1 -Installer
```

Omit `-Installer` for the portable release only. The script finds `makensis` on PATH or in its standard Windows installation folder; otherwise pass `-NsisCompiler 'C:\path\makensis.exe'`. The script builds, runs the disposable cleanup and process-lifecycle tests, publishes the self-contained win-x64 application, runs the non-destructive UI smoke check on Windows, packages the ZIP, optionally compiles the installer, and writes `dist/SHA256SUMS.txt`. Native command failures propagate as failures. Previous generated ZIP, installer, and checksum files are removed before building, and checksums cover only artifacts produced by the current run.

NSIS can also cross-compile the installer on Linux once the published executable exists. From the repository, run `makensis -DAPP_VERSION=1.0.0 installer/WindowsSystemMonitor.nsi`. The installer uses current-user permissions and HKCU uninstall registration; it does not elevate. Close the monitor before installing or uninstalling. The uninstaller removes only application-owned files and shortcuts, retaining any other files in the installation directory.

For development:

```powershell
dotnet run --project src/WindowsSystemMonitor
dotnet run --project tests/WindowsSystemMonitor.Tests -c Release
dotnet run --project tests/WindowsSystemMonitor.ProcessTests -c Release
```

The portable core and process tests also run on Linux with .NET 10; Windows-native deletion checks are explicitly skipped there. Linux can cross-compile the Windows project because `EnableWindowsTargeting` is enabled, but cannot validate the Windows UI, Windows APIs, UAC, or installation. The [Windows pipeline](.github/workflows/windows.yml) is configured for GitHub's Windows Server runner; adding the workflow does not mean it has run. Windows 10/11 manual validation is still required.

## Validation

See [actual validation results](docs/validation-results.md) and the [Windows acceptance checklist](docs/windows-acceptance.md). Performance targets are below 1% average idle CPU and preferably below 100 MB RAM (150 MB upper target); they are targets until measured. No Windows performance benchmark is implied by a successful build or smoke test.
