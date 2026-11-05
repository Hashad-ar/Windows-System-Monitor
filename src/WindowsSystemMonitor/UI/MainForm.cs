using System.Globalization;
using WindowsSystemMonitor.Core;
using WindowsSystemMonitor.Services;

namespace WindowsSystemMonitor.UI;

internal sealed class MainForm : Form
{
    private readonly MetricsService metrics = new();
    private readonly WindowsOperations operations = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 4000 };
    private readonly MetricCard cpu = new("Processor");
    private readonly MetricCard memory = new("Memory");
    private readonly MetricCard storage = new("System drive");
    private readonly Label network = Theme.Text("Network  ·  Waiting…", color: Theme.Muted);
    private readonly Label uptime = Theme.Text("Uptime  ·  Waiting…", color: Theme.Muted);
    private readonly Label system = Theme.Text("Reading system information…", color: Theme.Muted);
    private readonly Label status = Theme.Text("Ready. Monitoring refreshes every 4 seconds.", color: Theme.Muted);
    private readonly ProgressBar progress = new() { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 25, Visible = false, Height = 3, Dock = DockStyle.Bottom };
    private readonly List<Button> actionButtons = [];
    private readonly Button cancel;
    private CancellationTokenSource? currentOperation;
    private bool reading;
    private bool closing;
    private bool resourcesDisposed;
    private bool busy;
    private bool cleanupDeleting;
    private SystemSnapshot? latestSnapshot;
    private readonly bool smokeTest;
    internal int ExitCode { get; private set; }

    internal MainForm(bool smokeTest)
    {
        this.smokeTest = smokeTest;
        Theme.Apply(this);
        Text = "Windows System Monitor";
        ClientSize = new Size(850, 610);
        MinimumSize = new Size(700, 540);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { /* OS icon fallback */ }
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 12), ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        var header = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.TopDown };
        header.Controls.Add(Theme.Text("System Monitor", 21, true));
        header.Controls.Add(Theme.Text("Your PC, at a glance. Maintenance when you need it.", color: Theme.Muted));
        root.Controls.Add(header, 0, 0);

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(0, 10, 4, 0) };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.Controls.Add(Theme.Text("SYSTEM OVERVIEW", 9, true, Theme.Muted));
        var cards = new TableLayoutPanel { Dock = DockStyle.Top, Height = 130, ColumnCount = 3, Margin = new Padding(0, 2, 0, 16) };
        for (int i = 0; i < 3; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        cards.Controls.Add(cpu, 0, 0); cards.Controls.Add(memory, 1, 0); cards.Controls.Add(storage, 2, 0);
        content.Controls.Add(cards);
        var details = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 16) };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60)); details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        network.Dock = DockStyle.Fill; uptime.Dock = DockStyle.Fill;
        system.AutoSize = false; system.Height = 42; system.Dock = DockStyle.Top;
        details.Controls.Add(network, 0, 0); details.Controls.Add(uptime, 1, 0); details.Controls.Add(system, 0, 1); details.SetColumnSpan(system, 2);
        content.Controls.Add(details);
        content.Controls.Add(Theme.Text("SYSTEM MAINTENANCE", 9, true, Theme.Muted));
        var primary = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 2, 0, 8) };
        primary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); primary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        AddPrimary(primary, "Clean Temporary Files", "Preview stale files before removal", (_, _) => _ = CleanupAsync(), 0, 0);
        AddPrimary(primary, "Optimize Drives", "Open Windows media-aware optimization", (_, _) => Launch(WindowsTool.OptimizeDrives), 1, 0);
        AddPrimary(primary, "Check for Updates", "Windows and application updates", (_, _) => OpenUpdates(), 0, 1);
        AddPrimary(primary, "System Health Scan", "Verify Windows files · administrator", (_, _) => _ = HealthAsync(false), 1, 1);
        content.Controls.Add(primary);
        var more = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Visible = false, WrapContents = true, Margin = new Padding(0) };
        AddTool(more, "Storage Sense", WindowsTool.StorageSettings);
        AddTool(more, "Startup Applications", WindowsTool.StartupApps);
        AddTool(more, "Windows Security", WindowsTool.WindowsSecurity);
        AddTool(more, "System Information", WindowsTool.SystemInformation);
        var repair = Theme.Button("Advanced Repair", (_, _) => _ = HealthAsync(true)); actionButtons.Add(repair); more.Controls.Add(repair);
        var expand = Theme.Button("More Tools  +", (_, _) => { more.Visible = !more.Visible; });
        expand.Click += (_, _) => expand.Text = more.Visible ? "More Tools  −" : "More Tools  +";
        content.Controls.Add(expand); content.Controls.Add(more);
        scroll.Controls.Add(content); root.Controls.Add(scroll, 0, 1);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(0, 12, 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        status.AutoSize = false; status.Dock = DockStyle.Fill;
        footer.Controls.Add(status, 0, 0);
        cancel = Theme.Button("Cancel", (_, _) => currentOperation?.Cancel()); cancel.Visible = false;
        footer.Controls.Add(cancel, 1, 0);
        footer.Controls.Add(Theme.Button("Close", (_, _) => Close()), 2, 0);
        root.Controls.Add(footer, 0, 2); Controls.Add(root); Controls.Add(progress);
        operations.StatusChanged += OnOperationStatus;
        timer.Tick += async (_, _) => await RefreshMetricsAsync();
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized) timer.Stop();
            else if (!closing) { metrics.ResetCpuSample(); timer.Start(); }
        };
        Shown += async (_, _) =>
        {
            timer.Start(); await RefreshMetricsAsync();
            if (smokeTest)
            {
                try
                {
                    await Task.Delay(1200, lifetime.Token);
                    await RefreshMetricsAsync();
                    // Hidden child controls legitimately have no HWND yet. Exercise
                    // the expansion before verifying every maintenance control.
                    more.Visible = true;
                    expand.Text = "More Tools  −";
                    PerformLayout();
                    await Task.Delay(100, lifetime.Token);
                    ExitCode = ValidSmokeSnapshot(latestSnapshot) && cpu.IsHandleCreated &&
                        actionButtons.All(b => b.IsHandleCreated && b.Enabled && b.Width > 0 && b.Height > 0)
                        ? 0 : 2;
                }
                catch (Exception) { ExitCode = 2; }
                finally { if (!closing) Close(); }
            }
        };
    }

    private static bool ValidSmokeSnapshot(SystemSnapshot? snapshot) =>
        snapshot is { CpuPercent: >= 0 and <= 100, MemoryTotalBytes: > 0,
            MemoryPercent: >= 0 and <= 100, StorageTotalBytes: > 0,
            StoragePercent: >= 0 and <= 100, Uptime: not null };

    private void AddPrimary(TableLayoutPanel panel, string title, string description, EventHandler handler, int column, int row)
    {
        var b = Theme.Button(title + "\n" + description, handler);
        b.AutoSize = false; b.Dock = DockStyle.Fill; b.Height = 64;
        b.TextAlign = ContentAlignment.MiddleLeft;
        b.AccessibleName = title; b.AccessibleDescription = description;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(b, column, row); actionButtons.Add(b);
    }

    private void AddTool(FlowLayoutPanel panel, string title, WindowsTool tool)
    {
        var b = Theme.Button(title, (_, _) => Launch(tool)); actionButtons.Add(b); panel.Controls.Add(b);
    }

    private async Task RefreshMetricsAsync()
    {
        if (reading || closing || WindowState == FormWindowState.Minimized) return;
        reading = true;
        try
        {
            var s = await Task.Run(metrics.ReadSnapshot, lifetime.Token);
            if (closing) return;
            latestSnapshot = s;
            cpu.UpdateValue(s.CpuPercent is double c ? $"{c:0}%" : "—", s.CpuPercent is null ? "Waiting for the next sample" : "Total processor utilization", s.CpuPercent);
            memory.UpdateValue(s.MemoryPercent is double m ? $"{m:0}%" : "—", s.MemoryUsedBytes is ulong used && s.MemoryTotalBytes is ulong total ? $"{Bytes(used)} / {Bytes(total)}" : "Measurement unavailable", s.MemoryPercent);
            storage.UpdateValue(s.StoragePercent is double d ? $"{d:0}%" : "—", s.StorageFreeBytes is long free ? $"{Bytes(free)} free on {s.SystemDrive}" : "Measurement unavailable", s.StoragePercent);
            network.Text = "Network  ·  " + s.NetworkStatus;
            uptime.Text = "Uptime  ·  " + (s.Uptime is TimeSpan t ? $"{(int)t.TotalDays}d {t.Hours}h {t.Minutes}m" : "Unavailable");
            system.Text = $"{s.DeviceName}  ·  {s.WindowsVersion}  ·  {s.Architecture}\n{s.ProcessorName}";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!closing) status.Text = "Metrics unavailable: " + ex.Message; }
        finally { reading = false; }
    }

    internal static string Bytes(double bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        int i = 0; while (bytes >= 1024 && i < units.Length - 1) { bytes /= 1024; i++; }
        return bytes.ToString(i == 0 ? "0" : "0.0", CultureInfo.CurrentCulture) + " " + units[i];
    }

    private void Launch(WindowsTool tool)
    {
        try
        {
            operations.OpenTool(tool);
            status.Text = "Opened Windows tool. Windows handles any further actions and may remain open after this app closes.";
        }
        catch (Exception ex) { DarkDialog.ShowError(this, "Could not open Windows tool", ex.Message); }
    }

    private async Task PerformAsync(string title, bool cancellable, Func<CancellationToken, Task> action)
    {
        if (busy || closing) return;
        busy = true; currentOperation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        foreach (var button in actionButtons) button.Enabled = false;
        status.Text = title; progress.Visible = true; cancel.Visible = cancellable;
        try { await action(currentOperation.Token); }
        catch (OperationCanceledException) { if (!closing) status.Text = "Canceled. No further work will be started."; }
        catch (Exception ex)
        {
            if (!closing) { status.Text = title + " failed."; DarkDialog.ShowError(this, title, ex.Message); }
        }
        finally
        {
            currentOperation.Dispose(); currentOperation = null; busy = false; cleanupDeleting = false;
            if (!closing) { foreach (var button in actionButtons) button.Enabled = true; progress.Visible = false; cancel.Visible = false; }
        }
    }

    private Task CleanupAsync() => PerformAsync("Scanning stale temporary files…", true, async token =>
    {
        var cleaner = CleanupService.ForCurrentUser();
        var plan = await cleaner.ScanAsync(token);
        if (closing) return;
        string preview = $"Your local Temp folder:\n{plan.RootPath}\n\n{plan.Candidates.Count:N0} eligible files · {Bytes(plan.EstimatedBytes)} of file content.\n{plan.SkippedCount:N0} entries excluded or inaccessible."
            + (plan.IsTruncated ? "\nScan limit reached; only the candidates shown in this total are included." : "")
            + "\n\nOnly .tmp and .temp files older than 24 hours are eligible. Files are checked again before deletion. Recent, changed, locked, protected and linked files are skipped. Folders are retained.\n\nDeletion is permanent. Administrator access is not required.\n\nLegacy cleanup: stale user temp files are supported. Prefetch, browser data, documents, print jobs, system files and obsolete Windows paths are intentionally excluded. Use More Tools → Storage Sense for Windows-managed system cleanup.";
        if (plan.Candidates.Count == 0) { status.Text = "Scan complete. No eligible stale temporary files found."; DarkDialog.ShowInfo(this, "Nothing to clean", preview); return; }
        cancel.Visible = false;
        if (!DarkDialog.Confirm(this, "Review temporary files", preview, "Delete these files")) { status.Text = "Cleanup canceled. No files were deleted."; return; }
        cleanupDeleting = true; cancel.Visible = true; status.Text = "Removing confirmed temporary files…";
        var result = await cleaner.DeleteAsync(plan, token);
        if (closing) return;
        string summary = $"{(result.Canceled ? "Cleanup stopped" : "Cleanup completed")}.\n\nDeleted {result.DeletedCount:N0} files ({Bytes(result.DeletedBytes)} of file content).\nSkipped {result.SkippedCount:N0} candidates.\nLeft {result.UnprocessedCount:N0} candidates unprocessed.\n\nThis measures successfully deleted file content, not physical disk space reclaimed. Compression, hard links and concurrent Windows activity can change available disk space.";
        status.Text = $"{(result.Canceled ? "Canceled" : "Completed")} · {Bytes(result.DeletedBytes)} deleted · {result.SkippedCount:N0} skipped · {result.UnprocessedCount:N0} remaining.";
        DarkDialog.ShowInfo(this, "Cleanup result", summary); await RefreshMetricsAsync();
    });

    private Task HealthAsync(bool repair)
    {
        string title = repair ? "Repair Windows components" : "Verify Windows health";
        string explanation = repair
            ? "Runs DISM /Online /Cleanup-Image /RestoreHealth, then sfc /scannow if DISM succeeds. These commands modify Windows components and may download repair files using Windows servicing settings."
            : "Runs DISM /Online /Cleanup-Image /ScanHealth, then sfc /verifyonly. These are diagnostic checks; this action does not request repairs.";
        if (!DarkDialog.Confirm(this, title, explanation + "\n\nAdministrator approval is required for each command. Operations can take a long time. Native Windows consoles show command output; this app reports their exit codes. Save your work first.\n\nClosing this app can leave the current Windows command running. No restart will be initiated by this app.", repair ? "Start repair" : "Start scan")) return Task.CompletedTask;
        return PerformAsync(title + "…", false, async token =>
        {
            var result = await operations.RunHealthAsync(repair, token);
            if (!closing) { status.Text = result.Summary; DarkDialog.ShowInfo(this, result.Canceled ? "Operation canceled" : "Windows command result", result.Summary); }
        });
    }

    private void OpenUpdates()
    {
        using var dialog = new UpdatesDialog(operations, Launch);
        dialog.ShowDialog(this);
    }

    private void OnOperationStatus(string message)
    {
        if (closing || !IsHandleCreated) return;
        try { BeginInvoke((Action)(() => { if (!closing) status.Text = message; })); }
        catch (InvalidOperationException) { /* The window closed before the callback could be queued. */ }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (closing) { base.OnFormClosing(e); return; }
        if (operations.HasRunningWindowsOperation)
        {
            if (!DarkDialog.Confirm(this, "Exit while Windows is working?", "Windows is running " + operations.ActiveWindowsOperation + ".\n\nExit stops this app's monitoring and prevents subsequent commands from starting. The current Windows command or installer may continue independently. Do not shut down Windows while servicing is active.\n\nChoose Cancel to keep waiting for its result.", "Exit; leave Windows working")) { e.Cancel = true; return; }
        }
        else if (cleanupDeleting && !DarkDialog.Confirm(this, "Stop cleanup and exit?", "Already deleted files cannot be restored by this app. Remaining candidates will be canceled.", "Stop and exit")) { e.Cancel = true; return; }
        closing = true; timer.Stop(); lifetime.Cancel(); currentOperation?.Cancel(); operations.CancelOwnedOperations();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed)
        {
            resourcesDisposed = true;
            closing = true;
            timer.Stop();
            timer.Dispose();
            lifetime.Cancel();
            currentOperation?.Cancel();
            operations.StatusChanged -= OnOperationStatus;
            operations.Dispose();
            lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
}
