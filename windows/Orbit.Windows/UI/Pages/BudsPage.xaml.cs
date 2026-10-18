using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Orbit.Core;
using Orbit.Core.Buds;

namespace Orbit.UI.Pages;

public partial class BudsPage : UserControl
{
    OrbitCore Core => App.Core;
    readonly Dictionary<AncMode, RadioButton> _modes = new();
    string _soundKey = "", _tapKey = "";
    bool _binding;

    public BudsPage()
    {
        InitializeComponent();
        foreach (var m in AncModes.All)
        {
            var rb = new RadioButton
            {
                Style = (Style)FindResource("Segment"), GroupName = "anc",
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new Ic { Data = (Geometry)FindResource(m switch { AncMode.Off => "IcModeOff", AncMode.Transparency => "IcModeTrans", _ => "IcModeAnc" }), Width = 20, Height = 20, Margin = new Thickness(0, 0, 8, 0) },
                        new TextBlock { Text = m.Label(), VerticalAlignment = VerticalAlignment.Center }
                    }
                }
            };
            rb.Checked += (_, _) => { if (!_binding && Core.Buds.Anc != m) Core.BudsSetAnc(m); };
            AutomationPropertiesHelper.SetName(rb, m.Label());
            _modes[m] = rb;
            Modes.Children.Add(rb);
        }
        Core.BudsChanged += Refresh;
        Core.Phone.PropertyChanged += (_, _) => Refresh();
        App.Current.ThemeChanged += () => { _soundKey = _tapKey = ""; Refresh(); };
        SizeChanged += (_, _) => Layout2(ActualWidth >= 900);
        IsVisibleChanged += (_, _) => { if (IsVisible) Refresh(); };
        Refresh();
    }

    void Layout2(bool two)
    {
        Grid.SetColumn(RightCol, two ? 1 : 0);
        Grid.SetRow(RightCol, two ? 0 : 1);
        Grid.SetColumnSpan(LeftCol, two ? 1 : 2);
        Grid.SetColumnSpan(RightCol, two ? 1 : 2);
    }

    Brush B(string key) => (Brush)FindResource(key);

    void Refresh()
    {
        if (!IsLoaded && !IsVisible) return;
        _binding = true;
        try { Bind(); } finally { _binding = false; }
    }

    void Bind()
    {
        var s = Core.Buds;
        var local = Core.BudsLocal.State;
        var peer = Core.BudsPeer;
        bool phoneLinked = Core.Phone.IsConnected;
        bool phoneHas = phoneLinked && peer is { state.Connected: true };
        bool on = s.Connected;

        NameText.Text = s.Name;
        StatusDot.Fill = B(on ? "Green" : "AmberInk");
        StatusText.Foreground = B(on ? "GreenInk" : "AmberInk");
        StatusText.Text = (local.Connected, phoneHas) switch
        {
            (true, true) => $"Connected to this PC and {Core.Phone.Name}",
            (true, false) => "Connected to this PC",
            (false, true) => $"Connected to {Core.Phone.Name} · controlling through the phone",
            _ => Core.Settings.BudsMac == null ? "Choose your buds in Settings" : "Not connected · put them in your ears or open the case"
        };

        // Audio source cards
        bool pcPlaying = Core.PlayingHere, phonePlaying = peer?.playing == true;
        PhoneName.Text = phoneLinked ? Core.Phone.Name : "Phone";
        StyleSource(PcCard, PcState, PcPlay, local.Connected, pcPlaying, "this PC");
        StyleSource(PhoneCard, PhoneState, PhonePlay, phoneHas, phonePlaying, "your phone");

        // Battery
        BatteryGrid.Children.Clear();
        string[] labels = { "Left", "Right", "Case" };
        for (int i = 0; i < 3; i++)
        {
            int level = on ? s.Battery[i] : -1;
            var tile = new Border { Background = B("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Margin = new Thickness(5) };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = labels[i], FontSize = 12, FontWeight = FontWeights.Bold, Foreground = B("Text2") });
            sp.Children.Add(new TextBlock
            {
                Text = level < 0 ? "—" : $"{level}%" + (s.Charging[i] ? " ⚡" : ""), FontSize = 22, FontWeight = FontWeights.ExtraBold,
                Foreground = B(level < 0 ? "Text2" : "Text"), Margin = new Thickness(0, 4, 0, 6)
            });
            if (level >= 0)
                sp.Children.Add(new ProgressBar { Style = (Style)FindResource("Progress"), Value = level / 100.0, Height = 5, Foreground = B("Green") });
            else
                sp.Children.Add(new TextBlock { Text = i == 2 ? "Open lid to read" : "—", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = B("Text2") });
            tile.Child = sp;
            BatteryGrid.Children.Add(tile);
        }

        // Noise control
        foreach (var (m, rb) in _modes)
        {
            rb.IsChecked = s.Anc == m;
            rb.IsEnabled = on;
        }
        bool showCycle = on && s.CycleMask != 0 && s.Touch.Values.Contains(TouchAction.NoiseControl.Code);
        CyclePanel.Visibility = showCycle ? Visibility.Visible : Visibility.Collapsed;
        if (showCycle) BuildCycle(s);

        // Sound + taps (rebuilt only when the set of controls changes)
        var supported = Feature.All.Where(f => s.Features.ContainsKey(f.Id)).ToList();
        SoundCard.Visibility = supported.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var soundKey = string.Join(",", supported.Select(f => $"{f.Id}={s.Features[f.Id]}")) + on;
        if (soundKey != _soundKey) { _soundKey = soundKey; BuildSound(s, supported, on); }

        TapCard.Visibility = s.Touch.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var tapKey = string.Join(",", s.Touch.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")) + on;
        if (tapKey != _tapKey) { _tapKey = tapKey; BuildTaps(s, on); }

        bool empty = supported.Count == 0 && s.Touch.Count == 0;
        EmptyCard.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = on ? "Reading your buds…" : "Sound and tap settings";
        EmptyBody.Text = on ? "This takes a second the first time." :
            "They appear here once the buds are connected to this PC or your phone.";

        Footnote.Text = $"Settings live on the buds, so changes here also show on {(phoneLinked ? Core.Phone.Name : "your phone")}.";
    }

    void StyleSource(Border card, TextBlock state, Button play, bool connected, bool playing, string where)
    {
        card.BorderBrush = B(playing ? "Accent" : "CardBorder");
        card.Background = B(playing ? "Soft" : "Card");
        card.BorderThickness = new Thickness(playing ? 1.5 : 1);
        play.Visibility = connected && !playing ? Visibility.Visible : Visibility.Collapsed;
        state.Visibility = play.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        state.Foreground = B(playing ? "GreenInk" : "Text2");
        state.FontWeight = FontWeights.Bold;
        state.Text = playing ? "▮▮▮  Playing now" : connected ? "Connected · standby" : $"Buds not connected to {where}";
    }

    void BuildCycle(BudsSnapshot s)
    {
        CycleChips.Children.Clear();
        foreach (var m in AncModes.All)
        {
            bool inCycle = (s.CycleMask & (int)m) != 0;
            var tb = new Button
            {
                Content = (inCycle ? "✓ " : "") + m.Label(), Style = (Style)FindResource("Small"),
                Margin = new Thickness(0, 0, 8, 0),
                Background = B(inCycle ? "Soft" : "BtnBg"), Foreground = B(inCycle ? "SoftInk" : "Text"),
                BorderBrush = B(inCycle ? "Accent" : "BtnBorder")
            };
            tb.Click += (_, _) =>
            {
                int mask = Core.Buds.CycleMask ^ (int)m;
                if (System.Numerics.BitOperations.PopCount((uint)mask) < 2)
                    MessageBox.Show("Keep at least two modes in the cycle.", "Noise control");
                else Core.BudsSetCycle(mask);
                Refresh();
            };
            CycleChips.Children.Add(tb);
        }
    }

    void BuildSound(BudsSnapshot s, List<Feature> supported, bool on)
    {
        SoundList.Children.Clear();
        foreach (var f in supported)
        {
            var row = new DockPanel { Margin = new Thickness(12, 0, 12, 0), MinHeight = 52 };
            var sw = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = s.Features[f.Id], IsEnabled = on, Margin = new Thickness(12, 0, 0, 0) };
            AutomationPropertiesHelper.SetName(sw, f.Title);
            sw.Click += (_, _) => Core.BudsSetFeature(f.Id, sw.IsChecked == true);
            DockPanel.SetDock(sw, Dock.Right);
            row.Children.Add(sw);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 0, 8) };
            text.Children.Add(new TextBlock { Text = f.Title, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = B("Text") });
            text.Children.Add(new TextBlock { Text = f.Subtitle, FontSize = 12, Foreground = B("Text2"), TextWrapping = TextWrapping.Wrap });
            row.Children.Add(text);
            if (SoundList.Children.Count > 0) SoundList.Children.Add(new Border { Height = 1, Background = B("Track"), Margin = new Thickness(12, 0, 12, 0) });
            SoundList.Children.Add(row);
        }
    }

    void BuildTaps(BudsSnapshot s, bool on)
    {
        TapGrid.Children.Clear();
        TapGrid.RowDefinitions.Clear();
        int row = 0;
        void AddRow(UIElement a, UIElement? b, UIElement? c)
        {
            TapGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            foreach (var (el, col) in new[] { (a, 0), (b, 1), (c, 2) })
            {
                if (el == null) continue;
                Grid.SetRow(el, row); Grid.SetColumn(el, col);
                TapGrid.Children.Add(el);
            }
            row++;
        }
        TextBlock Head(string t, double size = 12, bool strong = false) => new()
        {
            Text = t, FontSize = size, FontWeight = strong ? FontWeights.ExtraBold : FontWeights.Bold,
            Foreground = B(strong ? "Text" : "Text2"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 10)
        };

        AddRow(Head("Tap actions", 15, true), Head("Left"), Head("Right"));
        var gestures = new[] { (BudsProtocol.Tap2, "Double tap"), (BudsProtocol.Tap3, "Triple tap"), (BudsProtocol.Hold, "Touch & hold") };
        foreach (var (g, title) in gestures)
        {
            var l = (BudsProtocol.SideLeft, g);
            var r = (BudsProtocol.SideRight, g);
            if (!s.Touch.ContainsKey(l) && !s.Touch.ContainsKey(r)) continue;
            AddRow(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = B("Text"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 10) },
                Picker(s, l, TouchAction.SingleBud, $"{title} left", on), Picker(s, r, TouchAction.SingleBud, $"{title} right", on));
        }
        var both = (BudsProtocol.SideBoth, BudsProtocol.Hold);
        if (s.Touch.ContainsKey(both))
            AddRow(new TextBlock { Text = "Hold both", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = B("Text"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 10) },
                Picker(s, both, TouchAction.BothBuds, "Hold both", on), null);
    }

    UIElement? Picker(BudsSnapshot s, (int side, int gesture) key, TouchAction[] options, string name, bool on)
    {
        if (!s.Touch.TryGetValue(key, out var code)) return null;
        var cb = new ComboBox { Style = (Style)FindResource("Drop"), Margin = new Thickness(0, 0, 10, 10), IsEnabled = on };
        AutomationPropertiesHelper.SetName(cb, name);
        foreach (var a in options) cb.Items.Add(a);
        cb.SelectedItem = options.FirstOrDefault(a => a.Code == code);
        if (cb.SelectedItem == null) { cb.Items.Add(new TouchAction(code, "Custom")); cb.SelectedIndex = cb.Items.Count - 1; }
        cb.SelectionChanged += (_, _) =>
        {
            if (cb.SelectedItem is TouchAction a && a.Code != code) Core.BudsSetTouch(key.side, key.gesture, a.Code);
        };
        return cb;
    }

    void PlayPc_Click(object s, RoutedEventArgs e) => Core.PlayOn(pc: true);
    void PlayPhone_Click(object s, RoutedEventArgs e) => Core.PlayOn(pc: false);
}

static class AutomationPropertiesHelper
{
    public static void SetName(DependencyObject o, string name) => System.Windows.Automation.AutomationProperties.SetName(o, name);
}
