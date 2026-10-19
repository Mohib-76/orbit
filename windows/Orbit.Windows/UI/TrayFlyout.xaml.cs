using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using Orbit.Core;
using Orbit.Core.Buds;
using Orbit.Core.Media;

namespace Orbit.UI;

public partial class TrayFlyout : Window
{
    OrbitCore Core => App.Core;

    public TrayFlyout()
    {
        InitializeComponent();
        DataContext = Core;
        Core.Phone.PropertyChanged += (_, _) => Refresh();
        Core.Notifications.CollectionChanged += (_, _) => Refresh();
        Core.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(OrbitCore.DoNotDisturb)) Refresh(); };
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] f) { Core.SendFiles(f); Hide(); }
        };
        foreach (var m in AncModes.All)
        {
            var rb = new RadioButton { Style = (Style)FindResource("Segment"), GroupName = "flyAnc", Content = m.Label(), Height = 28, FontSize = 11 };
            rb.Checked += (_, _) => { if (!_binding && Core.Buds.Anc != m) Core.BudsSetAnc(m); };
            System.Windows.Automation.AutomationProperties.SetName(rb, m.Label());
            _modes[m] = rb;
            BudsModes.Children.Add(rb);
        }
        Core.BudsChanged += RefreshBuds;
        Core.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(OrbitCore.BudsEnabled)) RefreshBuds(); };
        UsbSwitchHost.Content = new UsbModeSwitch(compact: true);
        Core.UsbChanged += () => { RefreshUsb(); Refresh(); };
        Core.PhoneMediaChanged += RefreshBuds;
        _mediaTimer.Tick += (_, _) => TickMedia();
        MBar.Seek += f => { if (Core.PhonePlayer is { } p) Core.PhoneMediaCommand("seek", (long)(f * p.LenMs)); };
        IsVisibleChanged += (_, _) => { if (IsVisible) RefreshBuds(); else _mediaTimer.Stop(); };
        Refresh();
        RefreshBuds();
        RefreshUsb();
    }

    readonly Dictionary<AncMode, RadioButton> _modes = new();
    readonly System.Windows.Threading.DispatcherTimer _mediaTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    bool _binding;

    void RefreshUsb()
    {
        UsbBox.Visibility = Core.UsbCable ? Visibility.Visible : Visibility.Collapsed;
        if (!Core.UsbCable) return;
        UsbUi.Apply(UsbUi.Describe(Core, compact: true), UsbIcon, UsbText, UsbLink);
        UsbOpen.Visibility = Core.UsbMode == Orbit.Core.UsbModes.Files && Core.UsbSwitchingTo == null && Core.UsbBlockedReason != "debugOff"
            ? Visibility.Visible : Visibility.Collapsed;
    }

    void UsbOpen_Click(object s, RoutedEventArgs e) { Hide(); Core.OpenPhoneInExplorer(); }
    void UsbLink_Click(object s, RoutedEventArgs e) { Hide(); UsbUi.OnLink(UsbLink.Content as string); }

    /// <summary>The "Listening" card: the phone's track and a seek slider on top, the buds under it.</summary>
    void RefreshMedia()
    {
        var p = Core.PhonePlayer;
        MediaRow.Visibility = p != null ? Visibility.Visible : Visibility.Collapsed;
        if (p == null) { _mediaTimer.Stop(); return; }
        var art = Core.PhoneArt(p.Art);
        MArt.ImageSource = art;
        MArtBox.Visibility = art != null ? Visibility.Visible : Visibility.Collapsed;
        var tint = MediaUi.Tint(p.App);
        MArtNone.Background = tint;
        MArtInitial.Text = MediaUi.Initial(p.App);
        MAppChip.Background = tint;
        MAppInitial.Text = MediaUi.Initial(p.App);
        MTitle.Text = p.Title.Length > 0 ? p.Title : p.App;
        MArtist.Text = p.Artist.Length > 0 ? " · " + p.Artist : "";
        bool inBuds = Core.BudsEnabled && Core.BudsPeer is { state.Connected: true } && !Core.BudsLocal.State.Connected;
        MWhere.Text = $"{p.App} on phone" + (inBuds ? " · in your buds" : "");
        bool control = Core.PhoneMedia.Controllable;
        MPlayIcon.Data = (Geometry)FindResource(p.Playing ? "IcPause" : "IcPlay");
        MPlay.IsEnabled = control && (p.Playing ? p.CanPause : p.CanPlay);
        MPrev.IsEnabled = control && p.CanPrev;
        MNext.IsEnabled = control && p.CanNext;
        MPrevIcon.SetResourceReference(Ic.IconFillProperty, MPrev.IsEnabled ? "Text" : "DisabledInk");
        MNextIcon.SetResourceReference(Ic.IconFillProperty, MNext.IsEnabled ? "Text" : "DisabledInk");
        MBar.CanSeek = control && p.CanSeek;
        MBar.SetResourceReference(SeekBar.FillProperty, p.IsLive ? "LiveRed" : p.Playing ? "Accent" : "MutedInk");
        MLen.Text = p.IsLive ? "Live" : MediaSnapshot.Clock(p.LenMs);
        MLen.SetResourceReference(TextBlock.ForegroundProperty, p.IsLive ? "LiveRed" : "Text2");
        TickMedia();
        if (p.Playing && IsVisible) _mediaTimer.Start(); else _mediaTimer.Stop();
    }

    void TickMedia()
    {
        if (Core.PhonePlayer is not { } p) return;
        var pos = Core.PhoneMedia.PositionNow(p, DateTime.UtcNow);
        MPos.Text = MediaSnapshot.Clock(pos);
        if (!MBar.IsDragging) MBar.Value = p.IsLive ? 1 : p.LenMs > 0 ? (double)pos / p.LenMs : 0;
    }

    void MediaPlay_Click(object s, RoutedEventArgs e) => Core.PhoneMediaCommand(Core.PhonePlayer?.Playing == true ? "pause" : "play");
    void MediaPrev_Click(object s, RoutedEventArgs e) => Core.PhoneMediaCommand("prev");
    void MediaNext_Click(object s, RoutedEventArgs e) => Core.PhoneMediaCommand("next");

    void RefreshBuds()
    {
        RefreshMedia();
        bool media = MediaRow.Visibility == Visibility.Visible;
        BudsPart.Visibility = Core.BudsEnabled ? Visibility.Visible : Visibility.Collapsed;
        BudsPart.BorderThickness = new Thickness(0, media ? 1 : 0, 0, 0);
        BudsCard.Visibility = Core.BudsEnabled || media ? Visibility.Visible : Visibility.Collapsed;
        if (!Core.BudsEnabled) return;
        var s = Core.Buds;
        bool pcHas = Core.BudsLocal.State.Connected, phonePlaying = Core.BudsPeer?.playing == true;
        BudsName.Text = s.Name.Replace("realme ", "");
        BudsInfo.Text = !s.Connected ? " · not connected"
            : $" · {s.BatterySummary}{(media ? "" : Core.PlayingHere ? " · on this PC" : phonePlaying ? " · on phone" : "")}";
        BudsPlayHere.Visibility = pcHas && !Core.PlayingHere && phonePlaying ? Visibility.Visible : Visibility.Collapsed;
        _binding = true;
        foreach (var (m, rb) in _modes) { rb.IsChecked = s.Anc == m; rb.IsEnabled = s.Connected; }
        _binding = false;
    }

    void BudsPlayHere_Click(object s, RoutedEventArgs e) => Core.PlayOn(pc: true);

    void Refresh()
    {
        var p = Core.Phone;
        bool on = p.State == LinkState.Connected;
        NameText.Text = string.IsNullOrEmpty(p.Name) ? "No phone paired" : p.Name;
        LinkText.Text = on ? (Core.UsbCable ? "Connected by USB" : $"Connected · {p.Link}") : p.StatusText;
        LinkText.Foreground = (Brush)FindResource(on ? "GreenInk" : "Text2");
        Dot.Fill = (Brush)FindResource(on ? "Green" : "AmberInk");
        BatteryNum.Text = p.Battery < 0 ? "–" : p.Battery.ToString();
        Notifs.ItemsSource = Core.Notifications.Take(4).ToList();
        NoNotifs.Visibility = Core.Notifications.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PauseText.Text = Core.DoNotDisturb ? "Resume" : "Pause";
    }

    public void ShowNearTray()
    {
        Refresh();
        var area = SystemParameters.WorkArea;
        Show();
        UpdateLayout();
        Left = area.Right - ActualWidth - 4;
        Top = area.Bottom - ActualHeight - 4;
        Activate();
    }

    void Clip_Click(object s, RoutedEventArgs e) { App.Current.SendClipboardNow(); Hide(); }
    void Mirror_Click(object s, RoutedEventArgs e) { Hide(); App.Current.OpenMirror(); }
    void File_Click(object s, RoutedEventArgs e) { Hide(); App.Current.PickAndSendFiles(); }
    void Pause_Click(object s, RoutedEventArgs e) => Core.PauseNotifications(Core.DoNotDisturb ? null : TimeSpan.FromHours(1));
    void Settings_Click(object s, RoutedEventArgs e) { Hide(); App.Current.ShowMain("settings"); }
    void Open_Click(object s, RoutedEventArgs e) { Hide(); App.Current.ShowMain(); }
    void ClearAll_Click(object s, RoutedEventArgs e) => Core.ClearNotifications();
    void Dismiss_Click(object s, RoutedEventArgs e) { if (s is FrameworkElement { Tag: string k }) Core.DismissNotification(k); }
}
