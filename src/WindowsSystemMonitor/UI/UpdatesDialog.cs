using WindowsSystemMonitor.Services;

namespace WindowsSystemMonitor.UI;

internal sealed class UpdatesDialog : Form
{
    private readonly WindowsOperations operations;
    private readonly CancellationTokenSource lifetime = new();
    private readonly TextBox output;
    private readonly TextBox packageId;
    private readonly Label state;
    private readonly Button check;
    private readonly Button update;
    private readonly Button cancel;
    private CancellationTokenSource? task;
    private bool closing;
    private bool resourcesDisposed;
    private bool busy;

    internal UpdatesDialog(WindowsOperations operations, Action<WindowsTool> launch)
    {
        this.operations = operations;
        Theme.Apply(this); Text = "Windows and application updates";
        ClientSize = new Size(790, 530); MinimumSize = new Size(640, 460);
        StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(22) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var title = Theme.Text("Updates, through Windows", 17, true); layout.Controls.Add(title, 0, 0);
        var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        commands.Controls.Add(Theme.Button("Windows Update", (_, _) => launch(WindowsTool.WindowsUpdate)));
        commands.Controls.Add(Theme.Button("Optional driver updates", (_, _) => launch(WindowsTool.OptionalUpdates)));
        check = Theme.Button("Check application updates", (_, _) => _ = CheckAsync(), true); commands.Controls.Add(check);
        layout.Controls.Add(commands, 0, 1);
        output = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
            WordWrap = false, BackColor = Theme.Panel, ForeColor = Theme.Foreground, BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9), Text = "Windows Update opens Settings; Windows handles the scan.\r\n\r\nCheck application updates to query Windows Package Manager (winget).\r\nIts native output shows names, versions and package IDs when available.\r\nIt does not cover every application, driver or firmware update.\r\n\r\nNo updates are installed automatically.\r\nSource terms may require a one-time review in Windows Terminal.", Margin = new Padding(0, 8, 0, 14) };
        layout.Controls.Add(output, 0, 2);
        layout.Controls.Add(Theme.Text("Optional update: copy an exact package ID from the results (one application at a time).", color: Theme.Muted), 0, 3);
        var selection = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        selection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); selection.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        packageId = new TextBox { Dock = DockStyle.Fill, BackColor = Theme.Panel, ForeColor = Theme.Foreground,
            BorderStyle = BorderStyle.FixedSingle, PlaceholderText = "Publisher.Application", Margin = new Padding(0, 7, 12, 0), AccessibleName = "Exact application package ID" };
        update = Theme.Button("Preview selected update…", (_, _) => _ = UpdateAsync());
        selection.Controls.Add(packageId, 0, 0); selection.Controls.Add(update, 1, 0); layout.Controls.Add(selection, 0, 4);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        state = Theme.Text("Ready", color: Theme.Muted); state.MaximumSize = new Size(440, 0);
        cancel = Theme.Button("Cancel check", (_, _) => task?.Cancel()); cancel.Visible = false;
        footer.Controls.Add(state); footer.Controls.Add(cancel); footer.Controls.Add(Theme.Button("Close", (_, _) => Close()));
        layout.Controls.Add(footer, 0, 5); Controls.Add(layout);
    }

    private Task CheckAsync() => RunAsync(async token =>
    {
        state.Text = "Checking applications…";
        var result = await operations.CheckApplicationUpdatesAsync(token);
        if (closing) return;
        output.Text = result.Output.ReplaceLineEndings("\r\n");
        state.Text = result.Summary;
    });

    private Task UpdateAsync()
    {
        string id = packageId.Text.Trim();
        if (id.Length == 0) { state.Text = "Enter one exact package ID from the check results."; return Task.CompletedTask; }
        return RunAsync(async token =>
        {
            state.Text = "Reading selected application's update…";
            var preview = await operations.PreviewApplicationUpdateAsync(id, token);
            if (closing) return;
            output.Text = preview.Output.ReplaceLineEndings("\r\n");
            state.Text = preview.Summary;
            if (preview.ExitCode != 0 || preview.Canceled) return;
            cancel.Visible = false;
            if (!DarkDialog.Confirm(this, "Review application update", $"Selected package: {id}\n\n{preview.Output}\n\nOnly this exact package ID will be passed to winget upgrade. Read the installed and available versions above. If no applicable upgrade is listed, cancel.\n\nThe application installer may require administrator approval, show license terms, close the application, or request a restart. Save your work. Windows Package Manager will download the publisher's installer. This app does not request a restart.\n\nClosing this window while an installer is running leaves that Windows operation running independently.", "Update this application")) { state.Text = "Update canceled. No installation requested."; return; }
            state.Text = "Windows Package Manager is updating the selected application…";
            var result = await operations.UpgradeApplicationAsync(id, token);
            if (!closing) { state.Text = result.Summary; DarkDialog.ShowInfo(this, "Application update result", result.Summary); }
        });
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (busy || closing) return;
        busy = true; check.Enabled = update.Enabled = packageId.Enabled = false; cancel.Visible = true;
        task = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        try { await action(task.Token); }
        catch (OperationCanceledException) { if (!closing) state.Text = "Canceled."; }
        catch (Exception ex) { if (!closing) { state.Text = "Operation failed."; DarkDialog.ShowError(this, "Application updates", ex.Message); } }
        finally
        {
            task.Dispose(); task = null; busy = false;
            if (!closing) { check.Enabled = update.Enabled = packageId.Enabled = true; cancel.Visible = false; }
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (closing) { base.OnFormClosing(e); return; }
        if (operations.HasRunningWindowsOperation && !DarkDialog.Confirm(this, "Stop waiting for the installer?", "The Windows-owned installer may continue after this window closes. Cancel to continue waiting and see its result.", "Leave installer running")) { e.Cancel = true; return; }
        closing = true; lifetime.Cancel(); task?.Cancel();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed)
        {
            resourcesDisposed = true;
            closing = true;
            lifetime.Cancel();
            task?.Cancel();
            lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
}
