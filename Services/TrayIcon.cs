using System.Windows;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace CS2Optimizer.Services;

/// <summary>Notification-area icon with a dark context menu.</summary>
public sealed class TrayIcon : IDisposable
{
    readonly Forms.NotifyIcon _icon;
    readonly Forms.ToolStripMenuItem _overlayItem;

    public event Action? OpenRequested;
    public event Action? OverlayToggleRequested;
    public event Action? ExitRequested;

    public TrayIcon()
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))!.Stream;
        var menu = new Forms.ContextMenuStrip { Renderer = new DarkRenderer(), ShowImageMargin = false, Font = new Drawing.Font("Segoe UI", 9.5f) };
        menu.Items.Add(new Forms.ToolStripMenuItem("Открыть CS2 Optimizer", null, (_, _) => OpenRequested?.Invoke()) { Font = new Drawing.Font("Segoe UI", 9.5f, Drawing.FontStyle.Bold) });
        _overlayItem = new Forms.ToolStripMenuItem("Ватермарка", null, (_, _) => OverlayToggleRequested?.Invoke());
        menu.Items.Add(_overlayItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Выход", null, (_, _) => ExitRequested?.Invoke()));
        menu.Opening += (_, _) => _overlayItem.Text = OverlayControl.IsRunning ? "Выключить ватермарку" : "Включить ватермарку";

        _icon = new Forms.NotifyIcon
        {
            Icon = new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize),
            Text = "CS2 Optimizer",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) OpenRequested?.Invoke(); };
    }

    public void Notify(string title, string text) => _icon.ShowBalloonTip(3000, title, text, Forms.ToolTipIcon.None);

    bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
    }

    sealed class DarkRenderer() : Forms.ToolStripProfessionalRenderer(new DarkColors())
    {
        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Drawing.Color.FromArgb(0xED, 0xED, 0xEE);
            base.OnRenderItemText(e);
        }
    }

    sealed class DarkColors : Forms.ProfessionalColorTable
    {
        static readonly Drawing.Color Bg = Drawing.Color.FromArgb(0x13, 0x13, 0x14);
        static readonly Drawing.Color Hover = Drawing.Color.FromArgb(0x24, 0x2A, 0x14);
        static readonly Drawing.Color Line = Drawing.Color.FromArgb(0x38, 0x38, 0x3C);
        public override Drawing.Color ToolStripDropDownBackground => Bg;
        public override Drawing.Color MenuBorder => Line;
        public override Drawing.Color MenuItemBorder => Drawing.Color.FromArgb(0xD4, 0xFF, 0x3F);
        public override Drawing.Color MenuItemSelected => Hover;
        public override Drawing.Color SeparatorDark => Line;
        public override Drawing.Color SeparatorLight => Bg;
        public override Drawing.Color ImageMarginGradientBegin => Bg;
        public override Drawing.Color ImageMarginGradientMiddle => Bg;
        public override Drawing.Color ImageMarginGradientEnd => Bg;
    }
}
