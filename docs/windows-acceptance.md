# Windows acceptance checklist

Run on disposable Windows 10 22H2 and Windows 11 machines with a standard user. Record OS build, application commit/version, display scale, and actual outcomes. Do not run repair, defragmentation, or broad cleanup on a real user's system just to test the interface.

## Automated checks

```powershell
./scripts/build.ps1 -Installer
```

The console test runners exercise disposable cleanup fixtures and bounded process execution/cancellation. The published executable's `--smoke-test` opens the real window, performs its internal non-destructive check, then closes. The smoke script requires successful exit within 30 seconds and verifies that no process at that executable path remains. It does not perform system maintenance.

## UI and monitoring

- Open at 100%, 125%, 150%, and 200% DPI. Resize to minimum size; check labels, status, dialogs, scrollbars, keyboard focus, tab order, and screen-reader names. Confirm dark colors and readable selection/disabled text.
- Compare CPU and RAM trends with Task Manager, and system-drive totals with Explorer. Allow for refresh timing. Verify uptime and Windows version; disconnected networking must not crash the app.
- Minimize for a minute; confirm polling pauses and resumes promptly after restoring. Simulate an unavailable drive/API where practical; unavailable data must not be invented.
- Measure steady idle process CPU over at least 60 seconds after warmup and working/private memory after five minutes. Record machine specs and methodology. Target below 1% average CPU and 100–150 MB memory; do not infer performance from code inspection.

## Cleanup (disposable fixtures only)

- Run the core tests before any manual deletion. Test cancellation during scanning and execution; a canceled preview must not delete anything.
- Verify old candidates, recent files, files changed since preview, locked files, read-only files, nested directories, and reparse points are handled as documented.
- Confirm preview and result counts, and distinguish deleted logical file bytes from observed physical drive-free-space changes.
- Confirm the temp root and subdirectories remain. User libraries, browser profiles, Prefetch, spool, Windows Update data, and system folders must not be targets.

## Windows integrations

- Open Optimize Drives, Windows Update, optional updates, Storage Settings, Startup Apps, Windows Security, and System Information. Confirm the target window, then close it manually.
- Test winget present, absent, no updates, available updates, offline/source error, and cancellation. Preview selected application and confirmation before installation. Do not accept source agreements or package installs silently.
- On a disposable VM, cancel UAC for scans/repairs and verify a friendly canceled result. Then test authorized read-only diagnostics and inspect the actual utility output.
- Test repair only in a disposable VM with a snapshot and explicit consent. Verify no automatic reboot and that failed/canceled operations are not marked repaired.

## Shutdown

- Close normal monitoring with X, Close, and Alt+F4 separately. Confirm all `WindowsSystemMonitor` processes disappear in Task Manager and no hidden helper remains.
- Close during cleanup scan and winget check; confirm safe cancellation and process disposal.
- Close while a Windows-owned diagnostic is active. Confirm the user can keep waiting or leave the Windows operation running; no false cancellation message and no forced servicing termination.
- Check Task Manager Startup, Services, and Task Scheduler for absence of application persistence. Reboot and confirm the application does not start itself.

## Distribution

- On a clean VM without a .NET installation, run the portable executable and verify launch/exit.
- Install as a standard user. Verify per-user location, desktop and Start Menu icons, and shortcuts launching the app.
- Upgrade/reinstall, then uninstall with the app both closed and open. Verify program and shortcuts removed, no automatic restart, no system configuration changes unrelated to the app.
- Confirm installer/uninstaller blocks while the main window is open, runs without UAC, registers only in HKCU, and preserves an unrelated fixture placed in the installation folder when uninstalling.
- Confirm unsigned-package messaging matches reality; never disable Defender, UAC, signature checks, or SmartScreen to make a test pass.
