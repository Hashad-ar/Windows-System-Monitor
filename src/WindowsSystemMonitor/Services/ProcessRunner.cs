using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace WindowsSystemMonitor.Services;

public sealed record CommandResult(int? ExitCode, string Output, string Summary, bool Canceled = false)
{
    public bool Succeeded => !Canceled && ExitCode == 0;
}

/// <summary>Owns only interruptible, read-only child processes. Never use for servicing or installers.</summary>
internal sealed class ProcessRunner : IDisposable
{
    private const int OutputLimit = 96 * 1024;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _sync = new();
    private readonly HashSet<Process> _processes = [];
    private bool _disposed;

    public async Task<CommandResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        var token = linked.Token;
        token.ThrowIfCancellationRequested();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Environment.SystemDirectory
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        Task<string>? stdout = null;
        Task<string>? stderr = null;

        try
        {
            // App execution aliases can take time to activate; do not block the UI thread.
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                lock (_sync)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    if (!process.Start()) throw new InvalidOperationException("Windows did not start the requested utility.");
                    _processes.Add(process);
                }
            }, token).ConfigureAwait(false);
            using var registration = token.Register(() => TryKill(process));
            // Drain both streams even once the display limit is reached, avoiding pipe deadlocks.
            stdout = ReadBoundedAsync(process.StandardOutput);
            stderr = ReadBoundedAsync(process.StandardError);
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            var output = await stdout.ConfigureAwait(false);
            var errors = await stderr.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(errors)) output += "\r\n" + errors;
            output = Regex.Replace(output, @"\x1B\[[0-?]*[ -/]*[@-~]", "");
            return new CommandResult(process.ExitCode, output.Trim(), $"Command finished with exit code {process.ExitCode} (0x{process.ExitCode:X8}).");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            // Read-only winget is app-owned: stop it before reporting cancellation.
            try { await process.WaitForExitAsync().ConfigureAwait(false); }
            catch (InvalidOperationException) { }
            if (stdout is not null && stderr is not null)
                await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            return new CommandResult(null, "", "Application update check canceled.", true);
        }
        finally
        {
            lock (_sync) _processes.Remove(process);
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        var truncated = false;
        int count;
        while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            var keep = Math.Min(count, OutputLimit - text.Length);
            if (keep > 0) text.Append(buffer, 0, keep);
            truncated |= keep < count;
        }
        if (truncated) text.AppendLine("\r\n[Output truncated. Use Windows Package Manager directly to view the complete list.]");
        return text.ToString();
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
    }

    public void CancelOwnedOperations()
    {
        lock (_sync)
            foreach (var process in _processes) TryKill(process);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _shutdown.Cancel();
        CancelOwnedOperations();
        _shutdown.Dispose();
    }
}
