using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using Orbit.Core;
using Orbit.Core.Buds;

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
        Core.BudsChanged += RefreshBuds;
        Core.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(OrbitCore.BudsEnabled)) RefreshBuds(); };
        Refresh();
        RefreshBuds();
    }

    readonly Dictionary<string, RadioButton> _modes = new();
    string _modesKey = "";
    bool _binding;

    void RefreshBuds()
    {
        BudsCard.Visibility = Core.BudsEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (!Core.BudsEnabled) return;
        var s = Core.Buds;
        bool pcHas = Core.BudsLocal.State.Connected, phonePlaying = Core.BudsPeer?.playing == true;
        BudsName.Text = s.Name.Replace("realme ", "");
        BudsInfo.Text = !s.Connected ? " · not connected"
            : $" · {s.BatterySummary}{(Core.PlayingHere ? " · on this PC" : phonePlaying ? " · on phone" : "")}";
        BudsPlayHere.Visibility = pcHas && !Core.PlayingHere && phonePlaying ? Visibility.Visible : Visibility.Collapsed;

        BudsModesBox.Visibility = s.NoiseModes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var key = string.Join(",", s.NoiseModes.Select(m => m.Id));
        if (key != _modesKey)
        {
            _modesKey = key;
            BudsModes.Children.Clear();
            _modes.Clear();
            BudsModes.Columns = Math.Max(1, s.NoiseModes.Count);
            foreach (var m in s.NoiseModes)
            {
                var rb = new RadioButton { Style = (Style)FindResource("Segment"), GroupName = "flyAnc", Content = m.Label, Height = 28, FontSize = 11 };
                rb.Checked += (_, _) => { if (!_binding && Core.Buds.Noise != m.Id) Core.BudsSetAnc(m.Id); };
                System.Windows.Automation.AutomationProperties.SetName(rb, m.Label);
                _modes[m.Id] = rb;
                BudsModes.Children.Add(rb);
            }
        }
        _binding = true;
        foreach (var (id, rb) in _modes) { rb.IsChecked = s.Noise == id; rb.IsEnabled = s.Control; }
        _binding = false;
    }

    void BudsPlayHere_Click(object s, RoutedEventArgs e) => Core.PlayOn(pc: true);

    void Refresh()
    {
        var p = Core.Phone;
        bool on = p.State == LinkState.Connected;
        NameText.Text = string.IsNullOrEmpty(p.Name) ? "No phone paired" : p.Name;
        LinkText.Text = on ? $"Connected · {p.Link}" : p.StatusText;
        LinkText.Foreground = (Brush)FindResource(on ? "GreenInk" : "Text2");
        Dot.Fill = (Brush)FindResource(on ? "Green" : "AmberInk");
        BatteryNum.Text = p.Battery < 0 ? "–" : p.Battery.ToString();
        Notifs.ItemsSource = Core.Notifications.Take(3).ToList();
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
