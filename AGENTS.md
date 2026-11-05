# Repository conventions

- C# / .NET 10; Windows Forms UI, dependency-free core and tests. Use the existing checkout; cloud tasks are already isolated. Do not create worktrees unless requested.
- Keep monitoring unelevated. Use only explicit Windows utility arguments; never interpolate commands into a shell.
- No telemetry, startup entries, services, scheduled tasks, tray persistence, registry cleaners, or automatic repairs.
- Cleanup must preview and confirm, exclude reparse points, revalidate candidates, skip recent/locked files, and never remove cleanup roots. Test only disposable fixtures.
- Use async operations, bounded output, cancellation, and deterministic disposal. Windows-owned operations may continue after exit only with clear user notice.
- Run `dotnet run --project tests/WindowsSystemMonitor.Tests -c Release` and `dotnet run --project tests/WindowsSystemMonitor.ProcessTests -c Release`, and build the Windows project after changes. Windows UI/integration checks require Windows; never claim unexecuted tests passed.
- Deliver self-contained win-x64 builds and an NSIS installer. Keep generated output under ignored directories.
