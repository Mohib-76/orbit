using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Orbit.Core;

namespace Orbit.UI;

/// <summary>
/// Charging · Tethering · File sharing. Shows the phone's current USB mode, a spinner while switching, and greys
/// out while switching isn't possible (USB mirroring, USB debugging off). Used on Home and in the tray flyout.
/// </summary>
public sealed class UsbModeSwitch : Border
{
    static OrbitCore Core => App.Core;
    readonly Dictionary<string, (RadioButton rb, Spinner spin)> _opts = new();
    bool _binding;
    static int _group;

    public UsbModeSwitch(bool compact)
    {
        SetResourceReference(BackgroundProperty, "Chip");
        CornerRadius = new CornerRadius(compact ? 7 : 10);
        Padding = new Thickness(1.5);
        var grid = new UniformGrid { Columns = 3 };
        var group = "usbMode" + _group++;
        foreach (var mode in UsbModes.All)
        {
            var spin = new Spinner { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            spin.SetResourceReference(Spinner.StrokeProperty, "Accent");
            var label = new TextBlock { Text = mode switch { UsbModes.Charging => "Charging", UsbModes.Tethering => "Tethering", _ => "File sharing" } };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(spin);
            content.Children.Add(label);
            var rb = new RadioButton
            {
                Style = (Style)Application.Current.FindResource("SegmentCard"), GroupName = group, Content = content,
                Height = compact ? 26 : 32, FontSize = compact ? 11 : 12
            };
            System.Windows.Automation.AutomationProperties.SetName(rb, UsbModes.Label(mode));
            rb.Checked += async (_, _) =>
            {
                if (_binding) return;
                await App.Current.RequestUsbMode(mode, Window.GetWindow(this));
                Refresh();
            };
            _opts[mode] = (rb, spin);
            grid.Children.Add(rb);
        }
        Child = grid;
        Core.UsbChanged += Refresh;
        Unloaded += (_, _) => Core.UsbChanged -= Refresh;
        Loaded += (_, _) => { Core.UsbChanged -= Refresh; Core.UsbChanged += Refresh; Refresh(); };
        Refresh();
    }

    public void Refresh()
    {
        var switching = Core.UsbSwitchingTo;
        var mode = Core.UsbMode;
        bool locked = Core.UsbBlockedReason is "mirroring" or "debugOff";
        _binding = true;
        foreach (var (m, (rb, spin)) in _opts)
        {
            rb.IsChecked = m == mode;
            rb.IsEnabled = !locked && (switching == null || m == switching);
            spin.Visibility = m == switching ? Visibility.Visible : Visibility.Collapsed;
        }
        _binding = false;
        Opacity = locked ? 0.55 : 1;
    }
}

/// <summary>The line under the USB switch: what the current mode means, or why it can't change.</summary>
public static class UsbUi
{
    public sealed record Status(string Text, string Icon, string Ink, string? Link);

    public static Status Describe(OrbitCore core, bool compact)
    {
        return core.UsbBlockedReason switch
        {
            "mirroring" => new("Stop mirroring to change USB mode", "IcInfo", "Text2", compact ? null : "Open mirror"),
            "debugOff" => new("Turn on USB debugging to choose", "IcInfo", "AmberInk", "How"),
            "switching" => new(compact ? "USB · switching…" : "Switching… the phone reconnects in a moment", "IcSwitching", "Text2", null),
            _ => core.UsbMode switch
            {
                UsbModes.Tethering => new(compact ? "USB · laptop online through phone" : "Laptop online through phone", "IcWifi", "GreenInk", null),
                UsbModes.Files => new(compact ? "USB · phone is in File Explorer" : "Unlock your phone if the folder looks empty", compact ? "IcCable" : "IcInfo", "Text2", null),
                UsbModes.Charging => new(compact ? "USB · charging only" : "Orbit stays connected over Wi-Fi", compact ? "IcCable" : "IcWifi", "Text2", null),
                _ => new(compact ? "USB · another mode" : "The phone is in another USB mode", "IcCable", "Text2", null)
            }
        };
    }

    public static void Apply(Status s, Ic icon, TextBlock text, Button link)
    {
        icon.Data = (Geometry)Application.Current.FindResource(s.Icon);
        icon.SetResourceReference(Control.ForegroundProperty, s.Ink);
        text.Text = s.Text;
        text.SetResourceReference(TextBlock.ForegroundProperty, s.Ink);
        link.Visibility = s.Link != null ? Visibility.Visible : Visibility.Collapsed;
        link.Content = s.Link ?? "";
    }

    public static void OnLink(string? link)
    {
        if (link == "How") App.OpenUrl(AppInfo.UsbDebuggingHelpUrl);
        else if (link == "Open mirror") App.Current.OpenMirror();
    }
}
