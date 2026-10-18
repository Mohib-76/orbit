using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Orbit.Core;

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
        Loaded += (_, _) => { RefreshPhone(); RefreshClip(); RefreshNotifs(); RefreshTransfer(); RefreshPause(); };
    }

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
