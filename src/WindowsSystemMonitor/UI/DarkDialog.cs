namespace WindowsSystemMonitor.UI;

internal sealed class DarkDialog : Form
{
    private DarkDialog(string title, string message, string accept, bool confirm)
    {
        Theme.Apply(this);
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(610, 380);
        MinimumSize = new Size(440, 300);
        MinimizeBox = false; MaximizeBox = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(Theme.Text(title, 16, true), 0, 0);
        var body = new TextBox { Text = message.ReplaceLineEndings("\r\n"), ReadOnly = true, Multiline = true, Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical, BackColor = Theme.Background,
            ForeColor = Theme.Foreground, Margin = new Padding(0, 8, 0, 20), TabStop = false };
        layout.Controls.Add(body, 0, 1);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var ok = Theme.Button(accept, (_, _) => { DialogResult = DialogResult.OK; Close(); }, true);
        buttons.Controls.Add(ok);
        if (confirm)
        {
            var cancel = Theme.Button("Cancel", (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
            buttons.Controls.Add(cancel); CancelButton = cancel; AcceptButton = cancel;
            Shown += (_, _) => cancel.Focus();
        }
        else { AcceptButton = ok; CancelButton = ok; }
        layout.Controls.Add(buttons, 0, 2); Controls.Add(layout);
    }

    internal static bool Confirm(IWin32Window? owner, string title, string message, string accept)
    {
        using var dialog = new DarkDialog(title, message, accept, true);
        return dialog.ShowDialog(owner) == DialogResult.OK;
    }

    internal static void ShowInfo(IWin32Window? owner, string title, string message)
    {
        using var dialog = new DarkDialog(title, message, "Close", false);
        dialog.ShowDialog(owner);
    }

    internal static void ShowError(IWin32Window? owner, string title, string message) => ShowInfo(owner, title, message);
}
