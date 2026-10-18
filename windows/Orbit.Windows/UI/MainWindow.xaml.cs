using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Orbit.Core;
using Orbit.UI.Pages;

namespace Orbit.UI;

public partial class MainWindow : Window
{
    readonly Dictionary<string, FrameworkElement> _pages = new();
    string _current = "home";
    OrbitCore Core => App.Core;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = Core;
        Core.Phone.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PhoneState.State) or nameof(PhoneState.Battery) or nameof(PhoneState.Name) or nameof(PhoneState.StatusText))
                Refresh();
        };
        Core.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OrbitCore.UnreadCount)) RefreshBadge();
            if (e.PropertyName == nameof(OrbitCore.BudsEnabled)) RefreshBudsNav();
        };
        RefreshBudsNav();
        Refresh();
        RefreshBadge();

        DragEnter += OnDragEnter;
        DragLeave += (_, _) => DropOverlay.Visibility = Visibility.Collapsed;
        Drop += OnDrop;
    }

    public void Navigate(string page)
    {
        var nav = page switch
        {
            "clipboard" => NavClipboard, "notifications" => NavNotifications, "files" => NavFiles,
            "buds" when Core.BudsEnabled => NavBuds, "settings" => NavSettings, _ => NavHome
        };
        if (nav.IsChecked == true) Show(page); else nav.IsChecked = true;
    }

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        // Fires during InitializeComponent for the default tab, before Host exists.
        if (Host != null && sender is RadioButton { Tag: string tag }) Show(tag);
    }

    void Show(string page)
    {
        _current = page;
        if (Core.Settings.Phone == null && page != "settings") page = "pair";
        if (!_pages.TryGetValue(page, out var view))
        {
            view = page switch
            {
                "pair" => new PairPage(),
                "clipboard" => new ClipboardPage(),
                "notifications" => new NotificationsPage(),
                "files" => new FilesPage(),
                "buds" => new BudsPage(),
                "settings" => new SettingsPage(),
                _ => new HomePage()
            };
            view.DataContext = Core;
            _pages[page] = view;
        }
        if (Host.Content != view) Host.Content = view;
    }

    void Refresh()
    {
        var p = Core.Phone;
        bool paired = Core.Settings.Phone != null;
        Show(_current);
        switch (p.State)
        {
            case LinkState.Connected:
                StatusPill.Background = (System.Windows.Media.Brush)FindResource("GreenSoft");
                StatusDot.Fill = (System.Windows.Media.Brush)FindResource("Green");
                StatusText.Foreground = (System.Windows.Media.Brush)FindResource("GreenInk");
                StatusText.Text = $"{p.Name} · {p.BatteryText}";
                break;
            case LinkState.Searching:
                StatusPill.Background = (System.Windows.Media.Brush)FindResource("AmberSoft");
                StatusDot.Fill = (System.Windows.Media.Brush)FindResource("AmberInk");
                StatusText.Foreground = (System.Windows.Media.Brush)FindResource("AmberInk");
                StatusText.Text = $"Looking for {p.Name}…";
                break;
            default:
                StatusPill.Background = (System.Windows.Media.Brush)FindResource("Surface2");
                StatusDot.Fill = (System.Windows.Media.Brush)FindResource("Text2");
                StatusText.Foreground = (System.Windows.Media.Brush)FindResource("Text2");
                StatusText.Text = paired ? "Not connected" : "No phone paired";
                break;
        }
    }

    void RefreshBudsNav()
    {
        NavBuds.Visibility = Core.BudsEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (!Core.BudsEnabled && NavBuds.IsChecked == true) NavHome.IsChecked = true;
    }

    void RefreshBadge()
    {
        var n = Core.UnreadCount;
        Badge.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        BadgeText.Text = n > 99 ? "99+" : n.ToString();
    }

    void OnDragEnter(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || Core.Settings.Phone == null) return;
        DropText.Text = $"Drop to send to {Core.Phone.Name}";
        DropOverlay.Visibility = Visibility.Visible;
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && Core.Settings.Phone != null)
        {
            Core.SendFiles(files);
            Navigate("files");
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Orbit keeps running in the tray.
        e.Cancel = true;
        Hide();
    }

    void Min_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Max_Click(object s, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object s, RoutedEventArgs e) => Close();

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        // Keep content inside the screen when maximized with a custom chrome.
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
    }
}
