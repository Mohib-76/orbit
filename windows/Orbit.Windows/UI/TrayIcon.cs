using System.ComponentModel;
using System.Runtime.InteropServices;
using Orbit.Core;
using D = System.Drawing;
using WF = System.Windows.Forms;

namespace Orbit.UI;

/// <summary>System tray icon with a status dot; left-click opens the flyout.</summary>
public sealed class TrayIcon : IDisposable
{
    readonly WF.NotifyIcon _icon;
    TrayFlyout? _flyout;
    D.Icon? _current;
    DateTime _flyoutClosedAt;

    public TrayIcon()
    {
        _icon = new WF.NotifyIcon { Visible = true, Text = "Orbit" };
        var menu = new WF.ContextMenuStrip();
        menu.Items.Add("Open Orbit", null, (_, _) => App.Current.ShowMain());
        menu.Items.Add("Send clipboard to phone", null, (_, _) => App.Current.SendClipboardNow());
        menu.Items.Add("Send files…", null, (_, _) => App.Current.PickAndSendFiles());
        var media = new WF.ToolStripMenuItem("Play/pause on phone", null, (_, _) => App.Current.TogglePhoneMedia());
        menu.Items.Add(media);
        var cycle = new WF.ToolStripMenuItem("Cycle noise control", null, (_, _) => App.Current.CycleNoiseMode());
        menu.Items.Add(cycle);
        menu.Opening += (_, _) =>
        {
            media.Visible = App.Core.PhonePlayer != null;
            cycle.Visible = App.Core.BudsEnabled;
        };
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("Quit Orbit", null, (_, _) => App.Current.Quit());
        _icon.ContextMenuStrip = menu;
        _icon.MouseClick += (_, e) => { if (e.Button == WF.MouseButtons.Left) ToggleFlyout(); };

        App.Core.Phone.PropertyChanged += OnPhoneChanged;
        Update();
    }

    void OnPhoneChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PhoneState.State) or nameof(PhoneState.Battery) or nameof(PhoneState.Name) or nameof(PhoneState.Charging))
            Update();
    }

    void Update()
    {
        var p = App.Core.Phone;
        var dot = p.State switch
        {
            LinkState.Connected => D.Color.FromArgb(0x1D, 0xB4, 0x6A),
            LinkState.Searching => D.Color.FromArgb(0xE0, 0x9B, 0x1A),
            _ => D.Color.FromArgb(0x8A, 0x8B, 0x9C)
        };
        var old = _current;
        _current = Draw(dot);
        _icon.Icon = _current;
        old?.Dispose();
        var text = p.State == LinkState.Connected
            ? $"Orbit · {p.Name} · {p.BatteryText}{(p.Charging ? " ⚡" : "")}"
            : $"Orbit · {p.StatusText}";
        _icon.Text = text.Length > 63 ? text[..63] : text;
    }

    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

    static D.Icon Draw(D.Color dot)
    {
        using var bmp = new D.Bitmap(32, 32);
        using (var g = D.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = D.Drawing2D.SmoothingMode.AntiAlias;
            using var accent = new D.SolidBrush(D.Color.FromArgb(0x4B, 0x4F, 0xE6));
            using var path = RoundRect(new D.RectangleF(1, 1, 30, 30), 7);
            g.FillPath(accent, path);
            using var white = new D.SolidBrush(D.Color.White);
            using var pen = new D.Pen(D.Color.White, 2.4f) { StartCap = D.Drawing2D.LineCap.Round, EndCap = D.Drawing2D.LineCap.Round };
            g.FillEllipse(white, 12.5f, 12.5f, 7, 7);
            g.DrawArc(pen, 6, 6, 20, 20, -30, 300);
            g.FillEllipse(white, 21.5f, 5.5f, 5, 5);
            using var ring = new D.SolidBrush(D.Color.White);
            g.FillEllipse(ring, 19, 19, 13, 13);
            using var d = new D.SolidBrush(dot);
            g.FillEllipse(d, 21, 21, 9, 9);
        }
        var h = bmp.GetHicon();
        var icon = (D.Icon)D.Icon.FromHandle(h).Clone();
        DestroyIcon(h);
        return icon;
    }

    static D.Drawing2D.GraphicsPath RoundRect(D.RectangleF r, float rad)
    {
        var p = new D.Drawing2D.GraphicsPath();
        p.AddArc(r.X, r.Y, rad * 2, rad * 2, 180, 90);
        p.AddArc(r.Right - rad * 2, r.Y, rad * 2, rad * 2, 270, 90);
        p.AddArc(r.Right - rad * 2, r.Bottom - rad * 2, rad * 2, rad * 2, 0, 90);
        p.AddArc(r.X, r.Bottom - rad * 2, rad * 2, rad * 2, 90, 90);
        p.CloseFigure();
        return p;
    }

    void ToggleFlyout()
    {
        if (_flyout is { IsVisible: true }) { _flyout.Hide(); return; }
        // Clicking the icon while the flyout is open deactivates it first; don't instantly reopen.
        if (DateTime.Now - _flyoutClosedAt < TimeSpan.FromMilliseconds(250)) return;
        if (_flyout == null)
        {
            _flyout = new TrayFlyout();
            _flyout.Deactivated += (_, _) => { _flyout.Hide(); _flyoutClosedAt = DateTime.Now; };
        }
        _flyout.ShowNearTray();
    }

    public void Balloon(string title, string text) => _icon.ShowBalloonTip(3000, title, text, WF.ToolTipIcon.None);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _current?.Dispose();
    }
}
