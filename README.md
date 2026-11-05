# Windows System Monitor — 1.0.0 preview 1 downloads

This branch contains the built packages. The full source code, tests, build scripts and documentation are on [main](https://github.com/Hashad-ar/Windows-System-Monitor/tree/main).

- [Download the Windows installer](https://github.com/Hashad-ar/Windows-System-Monitor/raw/refs/heads/downloads-v1.0.0-preview.1/WindowsSystemMonitor-Setup.exe)
- [Download the portable ZIP](https://github.com/Hashad-ar/Windows-System-Monitor/raw/refs/heads/downloads-v1.0.0-preview.1/WindowsSystemMonitor-Portable-win-x64.zip)
- [Download the standalone executable](https://github.com/Hashad-ar/Windows-System-Monitor/raw/refs/heads/downloads-v1.0.0-preview.1/portable/WindowsSystemMonitor.exe)
- [SHA-256 checksums](SHA256SUMS.txt)

Install using the setup executable, then launch the desktop shortcut. Alternatively, extract the portable ZIP and run WindowsSystemMonitor.exe. The application is self-contained; no separate .NET runtime installation is required.

These are **unsigned preview binaries awaiting Windows runtime testing**. Cross-compilation and package integrity checks succeeded, and 19 portable tests passed. Eight Windows-only cleanup tests were skipped. UI/DPI, UAC, Windows cleanup, installation/uninstallation and resource usage remain unverified. See [validation results](https://github.com/Hashad-ar/Windows-System-Monitor/blob/main/docs/validation-results.md). Do not disable Windows security protections to run the preview.

The installer is a standard x86 Unicode NSIS bootstrapper containing the x64 Windows application. It targets Windows 10 22H2 / Windows 11 and installs for the current user, with desktop and Start Menu shortcuts and an uninstaller.

GitHub Releases API access was unavailable in the upload environment, so these packages are stored on this download branch instead of Release assets.
