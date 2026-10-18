using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Orbit.Core;

namespace Orbit.UI;

/// <summary>One-time wireless mirroring setup (design: WinMirrorSetup).</summary>
public partial class MirrorSetupWindow : Window
{
    OrbitCore Core => App.Core;
    MirrorSetupState S => Core.MirrorSetup;
    bool _askedPhone;

    public MirrorSetupWindow()
    {
        InitializeComponent();
        S.PropertyChanged += OnSetupChanged;
        Core.Phone.PropertyChanged += OnPhoneChanged;
        Core.MirrorSetupDone += OnDone;
        SizeChanged += (_, _) => Reflow();
        Loaded += (_, _) => Core.BeginMirrorSetup();
        Refresh();
        RefreshPhone();
    }

    void OnSetupChanged(object? s, PropertyChangedEventArgs e) => Refresh();
    void OnPhoneChanged(object? s, PropertyChangedEventArgs e) => RefreshPhone();

    void OnDone()
    {
        App.Current.OpenMirror();
        Close();
    }

    /// <summary>Side by side when there's room, QR under the steps when narrow.</summary>
    void Reflow()
    {
        bool narrow = ActualWidth < 940;
        Grid.SetColumn(QrColumn, narrow ? 0 : 2);
        Grid.SetRow(QrColumn, narrow ? 1 : 0);
        QrColumn.Margin = narrow ? new Thickness(0, 28, 0, 0) : new Thickness(0);
        GapCol.Width = new GridLength(narrow ? 0 : 48);
    }

    void RefreshPhone()
    {
        var p = Core.Phone;
        bool on = p.State == LinkState.Connected;
        PhonePill.Background = (Brush)FindResource(on ? "GreenSoft" : "Surface2");
        PhoneDot.Fill = (Brush)FindResource(on ? "Green" : "Text2");
        PhoneText.Foreground = (Brush)FindResource(on ? "GreenInk" : "Text2");
        PhoneText.Text = on ? $"{p.Name} · {p.BatteryText}" : "Phone not connected";
    }

    void Refresh()
    {
        QrImage.Source = S.Qr;
        StatusText.Text = S.Status;
        var phone = string.IsNullOrEmpty(Core.Phone.Name) ? "your phone" : Core.Phone.Name;

        Steps.Children.Clear();
        bool dev = S.Known && S.DevOptions;
        if (dev) Steps.Children.Add(Row(0, "Developer options", $"Already on for {phone}", done: true));
        else Steps.Children.Add(Row(1, "Turn on Developer options", "Settings › About phone › Software information › tap Build number 7 times", active: S.Known));

        if (S.Known && S.WirelessDebugging)
            Steps.Children.Add(Row(0, "Wireless debugging", "On", done: true));
        else
            Steps.Children.Add(Row(2, "Open Wireless debugging",
                _askedPhone ? "Check your phone: it shows the next step" : "We'll open the right page on your phone",
                active: dev || !S.Known, action: ("Open on phone", OpenOnPhone)));

        Steps.Children.Add(Row(3, "Tap “Pair device with QR code”", "Then point the phone at the code on the right",
            active: S.Known && S.WirelessDebugging));

        WifiWarn.Visibility = S.Known && !S.Wifi ? Visibility.Visible : Visibility.Collapsed;
        WifiText.Text = $"{phone} isn't on Wi-Fi. Wireless mirroring needs the phone and this PC on the same Wi-Fi network; it doesn't work over the phone's own hotspot. Over USB it works right away.";
    }

    void OpenOnPhone()
    {
        Core.OpenMirrorSetupOnPhone();
        _askedPhone = true;
        Refresh();
    }

    /// <summary>One step row: a number (or a check when done), title, description and an optional button.</summary>
    FrameworkElement Row(int n, string title, string desc, bool done = false, bool active = false, (string label, Action click)? action = null)
    {
        var mark = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(0, 0, 14, 0) };
        if (done)
        {
            mark.Background = (Brush)FindResource("Green");
            mark.Child = new Ic { Data = (Geometry)FindResource("IcCheck"), Width = 16, Height = 16, Thickness = 3, Foreground = Brushes.White };
        }
        else
        {
            mark.Background = (Brush)FindResource(active ? "Accent" : "Soft");
            mark.Child = new TextBlock
            {
                Text = n.ToString(), FontSize = 14, FontWeight = FontWeights.ExtraBold,
                Foreground = (Brush)FindResource(active ? "OnAccent" : "SoftInk"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
        }

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.Bold });
        text.Children.Add(new TextBlock
        {
            Text = desc, FontSize = 13, TextWrapping = TextWrapping.Wrap,
            FontWeight = done ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = (Brush)FindResource(done ? "GreenInk" : "Text2")
        });

        var dock = new DockPanel();
        DockPanel.SetDock(mark, Dock.Left);
        dock.Children.Add(mark);
        if (action is var (label, click))
        {
            var b = new Button { Content = label, Style = (Style)FindResource(active ? "Primary" : "Secondary"), Height = 36, FontSize = 13, Margin = new Thickness(12, 0, 0, 0) };
            b.Click += (_, _) => click();
            DockPanel.SetDock(b, Dock.Right);
            dock.Children.Add(b);
        }
        dock.Children.Add(text);

        return new Border
        {
            Child = dock, Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(0, 0, 0, 10), CornerRadius = new CornerRadius(14),
            Background = (Brush)FindResource("Card"),
            BorderBrush = (Brush)FindResource(active ? "DropBorder" : "CardBorder"),
            BorderThickness = new Thickness(active ? 1.5 : 1)
        };
    }

    void UseUsb_Click(object s, RoutedEventArgs e)
    {
        if (Core.HasUsbMirrorDevice) { App.Current.OpenMirror(skipSetup: true); Close(); return; }
        UsbText.Text = Core.Adb.AnyUnauthorized()
            ? "Tap “Allow” on the USB debugging prompt on your phone, then click Use USB again."
            : "Plug your phone in with USB and turn on USB debugging (Developer options), then click Use USB again.";
    }

    void Min_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Close_Click(object s, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        S.PropertyChanged -= OnSetupChanged;
        Core.Phone.PropertyChanged -= OnPhoneChanged;
        Core.MirrorSetupDone -= OnDone;
        Core.EndMirrorSetup();
        base.OnClosed(e);
    }
}
