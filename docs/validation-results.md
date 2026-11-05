# Validation record

Final checks: November 4, 2026. Host: Linux x64 (Debian 13). SDK: Microsoft .NET 10.0.403, installed after SHA-512 verification against Microsoft's official release metadata. No third-party application packages.

## Executed successfully

- Full Windows Forms Release build: **0 warnings, 0 errors**.
- Cleanup console suite: **13 passed, 0 failed, 8 Windows-only skipped**. Exercises stale-extension selection, age boundary, accurate preview sizes, read-only exclusion, missing/invalid roots, cancellation, entry limits, symlink exclusion and preview ownership. Destructive tests use disposable directories only.
- Process console suite: **6 passed, 0 failed**. Exercises literal arguments, bounded concurrent stdout/stderr, real nonzero exit codes, cancellation/disposal stopping owned child processes, and unsafe package-ID rejection.
- Final self-contained **win-x64 single-file publish succeeded** with native runtime libraries embedded, compression enabled and trimming disabled.
- NSIS 3.11-1 installer compiler acquired from Debian using signed repository metadata and package checksums, without installing system packages. Final installer compilation succeeded with warnings treated as errors (`-WX`).
- Published executable PE header verified as x64 / Windows GUI; installer verified as a conventional x86 Unicode bootstrapper containing the x64 application. Portable ZIP CRCs verified; archived executable matches the published file byte-for-byte. SHA-256 hashes are in `dist/SHA256SUMS.txt`.
- Icon structure and seven embedded PNG checksums, SVG XML, CI YAML, installer references and PowerShell script structure checked. These static checks do not establish Windows execution or installation success.
- `scripts/setup-cloud.sh` executed successfully against the installed SDK, confirming the repeatable setup path builds and runs both suites. Initial SDK download and checksum verification were executed separately. The complete `scripts/package-cloud.sh` build/test/publish/installer/archive workflow also executed successfully. Full command logs: `artifacts/final-package.log`, `artifacts/installer-final.log`, `artifacts/validation.log` and `artifacts/publish.log` (local, ignored generated files).

## Produced artifacts

- `dist/portable/WindowsSystemMonitor.exe`: self-contained Windows executable.
- `dist/WindowsSystemMonitor-Portable-win-x64.zip`: executable and launch instructions.
- `dist/WindowsSystemMonitor-Setup.exe`: compiled per-user NSIS installer with desktop/Start Menu shortcuts and uninstall registration.
- `dist/SHA256SUMS.txt`: checksums for these generated artifacts.

The executable and installer are unsigned and have **not been executed on Windows**. Successful cross-compilation is not runtime validation.

## Not executed / still required on Windows

- Eight cleanup tests using native Windows handles, including deletion, changed/replaced/locked files, hard links, parent-link replacement, cancellation and repeat execution.
- Real UI opening and visual review, common DPI settings, keyboard/accessibility behavior, monitoring accuracy and unavailable-metric handling.
- Published `--smoke-test`, title-bar X / Close / Alt+F4 process disappearance, and no helper leaks on Windows. The Linux process tests validate owned fixture-child shutdown only.
- Native Windows Settings and Optimize Drives, winget behavior, UAC consent/cancellation, diagnostic and repair utility results. No maintenance command was run against this Linux host or real user system folders.
- Portable launch on a clean Windows PC without .NET installed.
- Running the compiled installer, desktop shortcut launch, update/reinstall and uninstallation on Windows.
- Idle CPU and RAM measurements. Below 1% CPU and 100–150 MB RAM remain targets, not verified benchmarks.
- GitHub Actions Windows workflow: included but not triggered. Windows Server CI does not replace Windows 10/11 acceptance checks.

Run `./scripts/build.ps1 -Installer` on Windows with the .NET 10 SDK and NSIS 3, then follow [the Windows acceptance checklist](windows-acceptance.md). Do not run repairs or broad cleanup as a routine smoke test. Windows runtime validation is required before treating this as a production-tested release.

## Environment configuration

Saved the complete tested `install_script` and `start_skill` in the cloud environment configuration draft. They retain the SDK/cache locations and reproduce the build/test workflow without a development server. Saving does not publish the environment; review/save the draft in environment settings and publish it to retain the prepared snapshot. Fresh-task restoration has not been verified.
