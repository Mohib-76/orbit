using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Orbit.Core;
using Orbit.Core.Media;

namespace Orbit.UI.Pages;

public partial class HomePage : UserControl
{
    OrbitCore Core => App.Core;
    Transfer? _watched;

    public HomePage()
    {
        InitializeComponent();
        Core.Phone.PropertyChanged += (_, _) => RefreshPhone();
        Core.ClipHistory.CollectionChanged += (_, _) => RefreshClip();
        Core.Notifications.CollectionChanged += (_, _) => RefreshNotifs();
        Core.Transfers.CollectionChanged += (_, _) => RefreshTransfer();
        Core.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(OrbitCore.DoNotDisturb)) RefreshPause(); };
        App.Current.ThemeChanged += RefreshPhone;
        SizeChanged += (_, _) => Cards.Columns = ActualWidth < 980 ? 1 : 3;
        DropZone.Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] f) { Core.SendFiles(f); e.Handled = true; }
        };
        Loaded += (_, _) => { RefreshPhone(); RefreshClip(); RefreshNotifs(); RefreshTransfer(); RefreshPause(); RefreshUsb(); RefreshMedia(); };

        UsbSwitchHost.Content = new UsbModeSwitch(compact: false);
        Core.UsbChanged += RefreshUsb;
        Core.PhoneMediaChanged += RefreshMedia;
        Seek.Seek += f => { if (Core.PhonePlayer is { } p) Core.PhoneMediaCommand("seek", (long)(f * p.LenMs)); };
        _mediaTimer.Tick += (_, _) => TickMedia();
        IsVisibleChanged += (_, _) => RefreshMedia();
    }

    // ---------------------------------------------------------------- USB mode

    void RefreshUsb()
    {
        UsbStrip.Visibility = Core.UsbCable ? Visibility.Visible : Visibility.Collapsed;
        if (!Core.UsbCable) return;
        UsbUi.Apply(UsbUi.Describe(Core, compact: false), UsbStatusIcon, UsbStatusText, UsbLink);
        UsbOpenFiles.Visibility = Core.UsbMode == UsbModes.Files && Core.UsbSwitchingTo == null && Core.UsbBlockedReason != "debugOff"
            ? Visibility.Visible : Visibility.Collapsed;
    }

    void UsbLink_Click(object s, RoutedEventArgs e) => UsbUi.OnLink(UsbLink.Content as string);
    void OpenPhoneFiles_Click(object s, RoutedEventArgs e) => Core.OpenPhoneInExplorer();

    // ---------------------------------------------------------------- playing on your phone

    readonly System.Windows.Threading.DispatcherTimer _mediaTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    void RefreshMedia()
    {
        var p = Core.PhonePlayer;
        MediaCard.Visibility = p == null ? Visibility.Collapsed : Visibility.Visible;
        if (p == null || !IsVisible) { _mediaTimer.Stop(); return; }

        var art = Core.PhoneArt(p.Art);
        ArtBrush.ImageSource = art;
        ArtBox.Visibility = art != null ? Visibility.Visible : Visibility.Collapsed;
        NoArtBox.Visibility = art == null ? Visibility.Visible : Visibility.Collapsed;
        var icon = MediaUi.AppIcon(p);
        var tint = MediaUi.Tint(p.App);
        NoArtChip.Background = tint; NoArtInitial.Text = MediaUi.Initial(p.App); NoArtIcon.Source = icon;
        AppChip.Background = tint; AppChipInitial.Text = MediaUi.Initial(p.App); AppChipIcon.Source = icon;
        AppName.Text = p.App;
        TrackTitle.Text = p.Title.Length > 0 ? p.Title : p.App;
        TrackSub.Text = MediaUi.Subtitle(p);
        TrackSub.Visibility = TrackSub.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        bool control = Core.PhoneMedia.Controllable;
        PlayIcon.Data = (Geometry)FindResource(p.Playing ? "IcPause" : "IcPlay");
        PlayBtn.IsEnabled = control && (p.Playing ? p.CanPause : p.CanPlay);
        PrevBtn.IsEnabled = control && p.CanPrev;
        NextBtn.IsEnabled = control && p.CanNext;
        PrevIcon.SetResourceReference(Ic.IconFillProperty, PrevBtn.IsEnabled ? "Text" : "DisabledInk");
        NextIcon.SetResourceReference(Ic.IconFillProperty, NextBtn.IsEnabled ? "Text" : "DisabledInk");

        Seek.CanSeek = control && p.CanSeek;
        Seek.SetResourceReference(SeekBar.FillProperty, p.IsLive ? "LiveRed" : p.Playing ? "Accent" : "MutedInk");
        LiveBadge.Visibility = p.IsLive ? Visibility.Visible : Visibility.Collapsed;
        LenText.Text = p.IsLive ? "" : MediaSnapshot.Clock(p.LenMs);

        var others = Core.PhoneMedia.Players;
        PlayerTabs.Visibility = others.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        PlayerTabsPanel.Children.Clear();
        if (others.Count > 1)
            foreach (var o in others)
            {
                var rb = new RadioButton
                {
                    Style = (Style)FindResource("SegmentCard"), Height = 26, FontSize = 11, Padding = new Thickness(8, 0, 8, 0),
                    GroupName = "phonePlayers", IsChecked = o.Id == p.Id, Content = TabContent(o)
                };
                System.Windows.Automation.AutomationProperties.SetName(rb, o.App);
                var id = o.Id;
                rb.Checked += (_, _) => { if (id != Core.PhonePlayer?.Id) Core.PickPhonePlayer(id); };
                PlayerTabsPanel.Children.Add(rb);
            }

        TickMedia();
        if (p.Playing) _mediaTimer.Start(); else _mediaTimer.Stop();
    }

    static StackPanel TabContent(PlayerInfo o)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 8, 0) };
        var chip = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(4), Background = MediaUi.Tint(o.App), Margin = new Thickness(0, 0, 6, 0) };
        chip.Child = new TextBlock { Text = MediaUi.Initial(o.App), Foreground = Brushes.White, FontSize = 8, FontWeight = FontWeights.ExtraBold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(chip);
        sp.Children.Add(new TextBlock { Text = o.App, VerticalAlignment = VerticalAlignment.Center });
        return sp;
    }

    void TickMedia()
    {
        if (Core.PhonePlayer is not { } p) return;
        var pos = Core.PhoneMedia.PositionNow(p, DateTime.UtcNow);
        PosText.Text = MediaSnapshot.Clock(pos);
        if (!Seek.IsDragging) Seek.Value = p.IsLive ? 1 : p.LenMs > 0 ? (double)pos / p.LenMs : 0;
    }

    void Play_Click(object s, RoutedEventArgs e) => Core.PhoneMediaCommand(Core.PhonePlayer?.Playing == true ? "pause" : "play");
    void Prev_Click(object s, RoutedEventArgs e) => Core.PhoneMediaCommand("prev");
    void Next_Click(object s, RoutedEventArgs e) => Core.PhoneMediaCommand("next");

    void RefreshPhone()
    {
        var p = Core.Phone;
        var connected = p.State == LinkState.Connected;
        Dot.Fill = (Brush)FindResource(connected ? "Green" : "AmberInk");
        StateText.Foreground = (Brush)FindResource(connected ? "GreenInk" : "AmberInk");
        LinkText.Text = connected ? p.Link : "Plug in USB, join your phone's hotspot, or share a Wi-Fi";
        LinkIcon.Data = (Geometry)FindResource(p.Link.Contains("hotspot") || p.Link.Contains("Wi-Fi") ? "IcWifi" : "IcUsb");
        DropLabel.Text = $"Drop files to send to {p.Name}";
    }

    void RefreshClip()
    {
        var c = Core.ClipHistory.FirstOrDefault();
        ClipText.Text = c?.Preview ?? "Nothing synced yet";
        ClipMeta.Text = c == null ? "Copy something to try it" : $"{c.Direction} · {Ago(c.Time)}";
    }

    void RefreshNotifs()
    {
        NotifList.ItemsSource = Core.Notifications.Take(3).ToList();
        NotifEmpty.Visibility = Core.Notifications.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NotifCount.Text = Core.Notifications.Count == 0 ? "Open" : $"{Core.Notifications.Count} new";
    }

    void RefreshTransfer()
    {
        var t = Core.Transfers.FirstOrDefault();
        if (_watched != null) _watched.PropertyChanged -= OnTransferChanged;
        _watched = t;
        if (t != null) t.PropertyChanged += OnTransferChanged;
        OnTransferChanged(null, null);
    }

    void OnTransferChanged(object? s, PropertyChangedEventArgs? e)
    {
        var t = _watched;
        LatestTransfer.Visibility = t == null ? Visibility.Collapsed : Visibility.Visible;
        if (t == null) return;
        LtName.Text = t.Name;
        LtDetail.Text = t.State == TransferState.Running ? $"{(int)(t.Progress * 100)}%" : t.State.ToString();
        LtProgress.Value = t.Progress;
    }

    void RefreshPause() => PauseText.Text = Core.DoNotDisturb ? "Resume notifications" : "Pause 1 h";

    static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        return d.TotalMinutes < 1 ? "just now" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes} min ago" : $"{(int)d.TotalHours} h ago";
    }

    void SendClipboard_Click(object s, RoutedEventArgs e) => App.Current.SendClipboardNow();
    void SendFiles_Click(object s, RoutedEventArgs e) => App.Current.PickAndSendFiles();
    void Mirror_Click(object s, RoutedEventArgs e) => App.Current.OpenMirror();
    void Pause_Click(object s, RoutedEventArgs e) => Core.PauseNotifications(Core.DoNotDisturb ? null : TimeSpan.FromHours(1));
    void OpenNotifications_Click(object s, RoutedEventArgs e) => App.Current.ShowMain("notifications");
    void OpenFiles_Click(object s, RoutedEventArgs e) => App.Current.ShowMain("files");
    void DropZone_Click(object s, MouseButtonEventArgs e) => App.Current.PickAndSendFiles();
    void SaveSettings(object s, RoutedEventArgs e) { Core.Settings.Save(); Core.SendSettings(); }
}
