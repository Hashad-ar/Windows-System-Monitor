# Windows System Monitor 1.0.0 preview 1

Native C#/.NET 10 Windows Forms utility for local monitoring and deliberate maintenance.

- CPU, memory, system-drive, network-link, uptime and device overview.
- Previewed cleanup of stale `.tmp`/`.temp` files with native handle checks.
- Windows Optimize Drives, Storage Settings, Security and update shortcuts.
- Winget application checks and individually confirmed updates.
- Separate elevated Windows diagnostics and repair; explicit process ownership and exit handling.
- Self-contained portable executable and per-user installer with shortcuts and uninstall support.

## Validation and status

This is an unsigned preview awaiting Windows runtime acceptance, not a production-validated release. Cross-build completed with zero warnings/errors; 19 tests passed on Linux and eight Windows-only cleanup tests were skipped. NSIS installer compilation and package integrity checks passed.

UI/DPI, native Windows cleanup, UAC/integrations, install/uninstall and performance still require Windows testing. The Windows CI workflow and acceptance checklist are included. See docs/validation-results.md for full evidence. Never disable Windows security protections to run the preview.

## Files

- `WindowsSystemMonitor-Setup.exe`: per-user installer; creates desktop and Start Menu shortcuts.
- `WindowsSystemMonitor-Portable-win-x64.zip`: extract and run `WindowsSystemMonitor.exe`.
- `SHA256SUMS.txt`: integrity hashes. Hashes do not authenticate the publisher.

End users do not need a separately installed .NET runtime. Source code and build scripts are available on the main branch.
