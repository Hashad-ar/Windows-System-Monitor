using System.Runtime.InteropServices;

namespace WindowsSystemMonitor.UI;

internal static class Theme
{
    internal static readonly Color Background = Color.FromArgb(20, 24, 30);
    internal static readonly Color Panel = Color.FromArgb(29, 35, 43);
    internal static readonly Color Border = Color.FromArgb(47, 58, 70);
    internal static readonly Color Foreground = Color.FromArgb(231, 237, 244);
    internal static readonly Color Muted = Color.FromArgb(158, 173, 189);
    internal static readonly Color Accent = Color.FromArgb(74, 196, 189);

    internal static void Apply(Form form)
    {
        form.BackColor = Background;
        form.ForeColor = Foreground;
        form.Font = new Font("Segoe UI", 9.5f);
        form.AutoScaleDimensions = new SizeF(96f, 96f);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.StartPosition = FormStartPosition.CenterScreen;
        form.HandleCreated += (_, _) =>
        {
            if (OperatingSystem.IsWindows())
            {
                int enabled = 1;
                _ = DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int));
            }
        };
    }

    internal static Label Text(string text, float size = 9.5f, bool bold = false, Color? color = null) => new()
    {
        Text = text, AutoSize = true, ForeColor = color ?? Foreground,
        Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        Margin = new Padding(0, 0, 0, 8)
    };

    internal static Button Button(string text, EventHandler action, bool primary = false)
    {
        var button = new Button
        {
            Text = text, AutoSize = true, MinimumSize = new Size(115, 36),
            Padding = new Padding(12, 5, 12, 5), FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Color.FromArgb(30, 77, 79) : Panel, ForeColor = Foreground,
            Cursor = Cursors.Hand, UseVisualStyleBackColor = false, Margin = new Padding(0, 0, 10, 8)
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(45, 62, 75);
        button.Click += action;
        return button;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

internal sealed class MetricCard : Panel
{
    private readonly Label value;
    private readonly Label detail;
    private double? percent;

    internal MetricCard(string title)
    {
        DoubleBuffered = true;
        BackColor = Theme.Panel;
        Padding = new Padding(16);
        Margin = new Padding(0, 0, 10, 0);
        Dock = DockStyle.Fill;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Panel };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(Theme.Text(title.ToUpperInvariant(), 8.5f, true, Theme.Muted), 0, 0);
        value = Theme.Text("—", 22, true);
        value.AutoSize = false; value.Dock = DockStyle.Fill;
        detail = Theme.Text("Waiting for Windows…", 8.5f, color: Theme.Muted);
        detail.AutoSize = false; detail.Dock = DockStyle.Fill;
        layout.Controls.Add(value, 0, 1); layout.Controls.Add(detail, 0, 2);
        Controls.Add(layout);
        AccessibleName = title;
    }

    internal void UpdateValue(string main, string caption, double? usage)
    {
        value.Text = main; detail.Text = caption; percent = usage;
        AccessibleDescription = main + ". " + caption;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        int inset = (int)(16 * DeviceDpi / 96f);
        using var track = new SolidBrush(Theme.Border);
        e.Graphics.FillRectangle(track, inset, Height - 10, Math.Max(0, Width - 2 * inset), 3);
        if (percent is double p)
        {
            using var fill = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(fill, inset, Height - 10, (int)((Width - 2 * inset) * Math.Clamp(p, 0, 100) / 100), 3);
        }
    }
}
