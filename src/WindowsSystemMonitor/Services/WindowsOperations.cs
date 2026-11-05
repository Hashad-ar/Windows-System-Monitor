using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace WindowsSystemMonitor.Services;

public enum WindowsTool
{
    OptimizeDrives, WindowsUpdate, OptionalUpdates, StorageSettings,
    StartupApps, WindowsSecurity, SystemInformation
}

public sealed record OperationResult(bool Completed, bool Canceled, string Summary);

/// <summary>
/// Launches a fixed set of Windows tools. Privileged commands are Windows-owned and are never killed.
/// Cancellation stops waiting and suppresses subsequent commands; it does not cancel Windows servicing.
/// </summary>
public sealed class WindowsOperations : IDisposable
{
    private readonly ProcessRunner _runner = new();
    private readonly SemaphoreSlim _windowsGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _sync = new();
    private sealed class ExternalOperation(Process process, string name)
    {
        public Process Process { get; } = process;
        public string Name { get; } = name;
        public bool Waiting { get; set; } = true;
    }
    private readonly List<ExternalOperation> _externalProcesses = [];
    private string? _launching;
    private bool _disposed;

    public event Action<string>? StatusChanged;

    public string? ActiveWindowsOperation
    {
        get
        {
            lock (_sync)
            {
                PruneExitedProcesses();
                return _launching ?? (_externalProcesses.Count == 0 ? null : string.Join(", ", _externalProcesses.Select(p => p.Name)));
            }
        }
    }

    public bool HasRunningWindowsOperation => ActiveWindowsOperation is not null;

    public void OpenTool(WindowsTool tool)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var target = tool switch
        {
            WindowsTool.OptimizeDrives => Path.Combine(Environment.SystemDirectory, "dfrgui.exe"),
            WindowsTool.WindowsUpdate => "ms-settings:windowsupdate",
            WindowsTool.OptionalUpdates => "ms-settings:windowsupdate-optionalupdates",
            WindowsTool.StorageSettings => "ms-settings:storagesense",
            WindowsTool.StartupApps => "ms-settings:startupapps",
            WindowsTool.WindowsSecurity => "windowsdefender:",
            WindowsTool.SystemInformation => "ms-settings:about",
            _ => throw new ArgumentOutOfRangeException(nameof(tool))
        };
        // Settings and Optimize Drives own any later work the user starts inside them.
        using var launched = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    public Task<CommandResult> CheckApplicationUpdatesAsync(CancellationToken token) =>
        RunWingetQueryAsync(["list", "--upgrade-available", "--source", "winget", "--disable-interactivity"], token);

    public Task<CommandResult> PreviewApplicationUpdateAsync(string packageId, CancellationToken token)
    {
        ValidatePackageId(packageId);
        return RunWingetQueryAsync(["list", "--id", packageId, "--exact", "--upgrade-available", "--source", "winget", "--disable-interactivity"], token);
    }

    private async Task<CommandResult> RunWingetQueryAsync(string[] arguments, CancellationToken token)
    {
        try
        {
            var result = await _runner.RunAsync(WingetPath(), arguments, token).ConfigureAwait(false);
            if (result.Canceled) return result;
            if (result.ExitCode == 0)
                return result with { Summary = "Windows Package Manager check completed. The native table shows installed and available versions; winget does not cover every application or driver." };
            return result with
            {
                Summary = $"Windows Package Manager could not complete the check (0x{result.ExitCode:X8}). " +
                          "Read its output below. If it requests source agreements, run 'winget list --upgrade-available --source winget' in Windows Terminal, review the terms, then retry. Managed-device policy may restrict access."
            };
        }
        catch (OperationCanceledException) { return new CommandResult(null, "", "Application update check canceled.", true); }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            return new CommandResult(null, "", "Windows Package Manager is unavailable. Install or update Microsoft App Installer from Microsoft Store, or use each application's own updater. " + ex.Message);
        }
    }

    public async Task<OperationResult> RunHealthAsync(bool repair, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
        await _windowsGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            if (HasRunningWindowsOperation)
                return new OperationResult(false, false, "A previous Windows operation is still running: " + ActiveWindowsOperation + ". Wait for it to finish before starting another scan or repair.");
            var dism = await RunWindowsCommandAsync("dism.exe", ["/Online", "/Cleanup-Image", repair ? "/RestoreHealth" : "/ScanHealth"],
                repair ? "DISM component repair" : "DISM component scan", true, linked.Token).ConfigureAwait(false);
            if (!dism.Completed) return dism;
            linked.Token.ThrowIfCancellationRequested();
            var sfc = await RunWindowsCommandAsync("sfc.exe", [repair ? "/scannow" : "/verifyonly"],
                repair ? "System File Checker repair" : "System File Checker verification", true, linked.Token).ConfigureAwait(false);
            if (!sfc.Completed) return sfc with { Summary = dism.Summary + "\r\n" + sfc.Summary };
            return new OperationResult(true, false, dism.Summary + "\r\n" + sfc.Summary +
                "\r\nCommands finished. SFC's exit code alone does not prove that Windows is healthy or repaired. Review Windows logs for findings: %windir%\\Logs\\DISM\\dism.log and %windir%\\Logs\\CBS\\CBS.log. No restart was requested by this app.");
        }
        catch (OperationCanceledException)
        {
            return new OperationResult(false, true, "Stopped waiting. A Windows command already started may continue independently; no further command will be launched.");
        }
        finally { _windowsGate.Release(); }
    }

    public async Task<OperationResult> UpgradeApplicationAsync(string packageId, CancellationToken token)
    {
        ValidatePackageId(packageId);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
        await _windowsGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            if (HasRunningWindowsOperation)
                return new OperationResult(false, false, "A previous Windows operation is still running: " + ActiveWindowsOperation + ". Wait for it to finish before starting another installation.");
            return await RunWindowsCommandAsync(WingetPath(),
                ["upgrade", "--id", packageId, "--exact", "--source", "winget", "--interactive"],
                $"Application update: {packageId}", false, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new OperationResult(false, true, "Stopped waiting. Windows Package Manager or an installer already launched may continue independently.");
        }
        finally { _windowsGate.Release(); }
    }

    private async Task<OperationResult> RunWindowsCommandAsync(string executable, string[] arguments, string name, bool elevate, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        Process? process = null;
        lock (_sync) _launching = name;
        Notify($"{name}: {(elevate ? "approve the Windows administrator prompt; " : "")}output appears in a separate Windows console.");
        try
        {
            var path = Path.IsPathRooted(executable) ? executable : Path.Combine(Environment.SystemDirectory, executable);
            var start = new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.SystemDirectory,
                WindowStyle = ProcessWindowStyle.Normal
            };
            if (elevate) start.Verb = "runas";
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            // ShellExecute can wait for UAC. It must never run on the UI thread.
            process = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                return Process.Start(start);
            }).ConfigureAwait(false);
            if (process is null) return new OperationResult(false, false, $"{name}: Windows did not return a process handle; completion cannot be verified.");
            lock (_sync)
            {
                if (!_disposed) _externalProcesses.Add(new ExternalOperation(process, name));
            }
            Notify($"{name} is running. Windows owns this operation; closing the monitor will not interrupt it.");
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            var exitCode = process.ExitCode;
            var completed = exitCode is 0 or 3010;
            var summary = $"{name} {(completed ? "finished" : "failed or was interrupted")} with exit code {exitCode} (0x{exitCode:X8}).";
            if (exitCode == 3010) summary += " Windows reports that a restart is required. Save your work and restart manually when convenient.";
            OperationLog.Write(executable.Contains("winget", StringComparison.OrdinalIgnoreCase) ? "winget" : executable, exitCode);
            return new OperationResult(completed, false, summary);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new OperationResult(false, true, $"{name} canceled at the Windows administrator prompt. The command was not started.");
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            return new OperationResult(false, false, $"{name} could not complete. {ex.Message}");
        }
        finally
        {
            lock (_sync)
            {
                _launching = null;
                var tracked = _externalProcesses.Find(p => ReferenceEquals(p.Process, process));
                if (tracked is not null) tracked.Waiting = false;
                if (process is not null && (_disposed || HasExited(process)))
                {
                    _externalProcesses.RemoveAll(p => ReferenceEquals(p.Process, process));
                    process.Dispose();
                }
            }
        }
    }

    private static string WingetPath()
    {
        // Never resolve an executable in the application's working directory or a user-supplied PATH entry.
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "Microsoft", "WindowsApps", "winget.exe");
    }

    private static void ValidatePackageId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 200 || !Regex.IsMatch(id, @"\A[A-Za-z0-9][A-Za-z0-9._+-]*\z"))
            throw new ArgumentException("Enter the complete application ID from the Windows Package Manager results, for example Publisher.Application. Spaces, truncated IDs, and command options are not accepted.", nameof(id));
    }

    private void Notify(string message) => StatusChanged?.Invoke(message);

    private static bool HasExited(Process process)
    {
        try { return process.HasExited; }
        catch (InvalidOperationException) { return true; }
    }

    private void PruneExitedProcesses()
    {
        for (var i = _externalProcesses.Count - 1; i >= 0; i--)
            if (!_externalProcesses[i].Waiting && HasExited(_externalProcesses[i].Process))
            {
                _externalProcesses[i].Process.Dispose();
                _externalProcesses.RemoveAt(i);
            }
    }

    public void CancelOwnedOperations() => _runner.CancelOwnedOperations();

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _shutdown.Cancel();
        _runner.Dispose();
        lock (_sync)
        {
            // Dispose handles only. Killing Windows servicing or an installer can damage the system.
            foreach (var item in _externalProcesses.Where(p => !p.Waiting)) item.Process.Dispose();
            _externalProcesses.RemoveAll(p => !p.Waiting);
        }
    }
}
