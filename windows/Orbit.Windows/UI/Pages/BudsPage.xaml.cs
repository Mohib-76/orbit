using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Orbit.Core;
using Orbit.Core.Buds;

namespace Orbit.UI.Pages;

public partial class BudsPage : UserControl
{
    OrbitCore Core => App.Core;
    readonly Dictionary<string, RadioButton> _modes = new();
    string _modesKey = "", _soundKey = "", _tapKey = "";
    bool _binding;

    public BudsPage()
    {
        InitializeComponent();
        Core.BudsChanged += Refresh;
        Core.Phone.PropertyChanged += (_, _) => Refresh();
        App.Current.ThemeChanged += () => { _modesKey = _soundKey = _tapKey = ""; Refresh(); };
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
        bool connected = s.Connected;
        bool on = s.Control; // the controls are live

        NameText.Text = s.Name;
        StatusDot.Fill = B(connected ? "Green" : "AmberInk");
        StatusText.Foreground = B(connected ? "GreenInk" : "AmberInk");
        StatusText.Text = (local.Connected, phoneHas) switch
        {
            (true, true) => $"Connected to this PC and {Core.Phone.Name}",
            (true, false) => "Connected to this PC",
            (false, true) => $"Connected to {Core.Phone.Name}" + (Core.BudsViaPhone ? " · controlling through the phone" : ""),
            _ => Core.Settings.BudsMac == null ? "Choose your buds in Settings" : "Not connected · put them in your ears or open the case"
        };

        // Audio source cards
        bool pcPlaying = Core.PlayingHere, phonePlaying = peer?.playing == true;
        PhoneName.Text = phoneLinked ? Core.Phone.Name : "Phone";
        StyleSource(PcCard, PcState, PcPlay, local.Connected, pcPlaying, "this PC");
        StyleSource(PhoneCard, PhoneState, PhonePlay, phoneHas, phonePlaying, "your phone");

        // Battery: left / right / case from the driver, else the one level Windows or the phone has.
        BatteryGrid.Children.Clear();
        if (connected && !s.HasSideBattery && s.BatteryMain >= 0)
        {
            BatteryGrid.Columns = 1;
            BatteryGrid.Children.Add(BatteryTile("Battery", s.BatteryMain, false, ""));
        }
        else
        {
            BatteryGrid.Columns = 3;
            string[] labels = { "Left", "Right", "Case" };
            for (int i = 0; i < 3; i++)
                BatteryGrid.Children.Add(BatteryTile(labels[i], connected ? s.Battery[i] : -1, s.Charging[i], i == 2 ? "Open lid to read" : "—"));
        }

        // Noise control (rebuilt only when the set of modes changes)
        NoiseCard.Visibility = s.NoiseModes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var modesKey = string.Join(",", s.NoiseModes.Select(m => m.Id));
        if (modesKey != _modesKey) { _modesKey = modesKey; BuildModes(s); }
        foreach (var (id, rb) in _modes)
        {
            rb.IsChecked = s.Noise == id;
            rb.IsEnabled = on;
        }
        bool showCycle = on && s.Driver == HeyMelodyDriver.DriverId && s.CycleMask != 0 && s.Touch.Values.Contains(TouchAction.NoiseControl.Code);
        CyclePanel.Visibility = showCycle ? Visibility.Visible : Visibility.Collapsed;
        if (showCycle) BuildCycle(s);

        // Sound + taps (rebuilt only when the set of controls changes)
        SoundCard.Visibility = s.Toggles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var soundKey = string.Join(",", s.Toggles.Select(t => $"{t.Id}={t.On}")) + on;
        if (soundKey != _soundKey) { _soundKey = soundKey; BuildSound(s, on); }

        TapCard.Visibility = s.Touch.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var tapKey = string.Join(",", s.Touch.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")) + on;
        if (tapKey != _tapKey) { _tapKey = tapKey; BuildTaps(s, on); }

        // Why controls are missing: not connected, no driver for these buds, a channel we can't open, or still reading.
        bool empty = s.NoiseModes.Count == 0 && s.Toggles.Count == 0 && s.Touch.Count == 0;
        (string title, string body)? note =
            !connected ? ("Sound and tap settings", "They appear here once the buds are connected to this PC or your phone.")
            : on ? (empty ? ("Reading your buds…", "This takes a second the first time.") : null)
            : s.Driver == null ? ("Battery and playback", "Orbit doesn't have controls for these buds yet. Noise control and sound settings work with realme, OnePlus and OPPO buds.")
            : ("Can't reach the buds' controls", "Another earbuds app may be using them. Close it, or put the buds in the case and take them out again.");
        EmptyCard.Visibility = note != null ? Visibility.Visible : Visibility.Collapsed;
        if (note is { } n) { EmptyTitle.Text = n.title; EmptyBody.Text = n.body; }

        FootnoteRow.Visibility = s.Driver != null ? Visibility.Visible : Visibility.Collapsed;
        Footnote.Text = $"Settings live on the buds, so changes here also show on {(phoneLinked ? Core.Phone.Name : "your phone")}.";
    }

    Border BatteryTile(string label, int level, bool charging, string unknown)
    {
        var tile = new Border { Background = B("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Margin = new Thickness(5) };
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = B("Text2") });
        sp.Children.Add(new TextBlock
        {
            Text = level < 0 ? "—" : $"{level}%" + (charging ? " ⚡" : ""), FontSize = 22, FontWeight = FontWeights.ExtraBold,
            Foreground = B(level < 0 ? "Text2" : "Text"), Margin = new Thickness(0, 4, 0, 6)
        });
        if (level >= 0)
            sp.Children.Add(new ProgressBar { Style = (Style)FindResource("Progress"), Value = level / 100.0, Height = 5, Foreground = B("Green") });
        else
            sp.Children.Add(new TextBlock { Text = unknown, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = B("Text2") });
        tile.Child = sp;
        return tile;
    }

    void BuildModes(BudsSnapshot s)
    {
        Modes.Children.Clear();
        _modes.Clear();
        Modes.Columns = Math.Max(1, s.NoiseModes.Count);
        foreach (var m in s.NoiseModes)
        {
            var rb = new RadioButton
            {
                Style = (Style)FindResource("Segment"), GroupName = "anc",
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new Ic { Data = (Geometry)FindResource(m.Id switch { "OFF" => "IcModeOff", "TRANSPARENCY" => "IcModeTrans", _ => "IcModeAnc" }), Width = 20, Height = 20, Margin = new Thickness(0, 0, 8, 0) },
                        new TextBlock { Text = m.Label, VerticalAlignment = VerticalAlignment.Center }
                    }
                }
            };
            rb.Checked += (_, _) => { if (!_binding && Core.Buds.Noise != m.Id) Core.BudsSetAnc(m.Id); };
            AutomationPropertiesHelper.SetName(rb, m.Label);
            _modes[m.Id] = rb;
            Modes.Children.Add(rb);
        }
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

    void BuildSound(BudsSnapshot s, bool on)
    {
        SoundList.Children.Clear();
        foreach (var f in s.Toggles)
        {
            var row = new DockPanel { Margin = new Thickness(12, 0, 12, 0), MinHeight = 52 };
            var sw = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = f.On, IsEnabled = on, Margin = new Thickness(12, 0, 0, 0) };
            AutomationPropertiesHelper.SetName(sw, f.Title);
            sw.Click += (_, _) => Core.BudsSetFeature(f.Id, sw.IsChecked == true);
            DockPanel.SetDock(sw, Dock.Right);
            row.Children.Add(sw);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 0, 8) };
            text.Children.Add(new TextBlock { Text = f.Title, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = B("Text") });
            if (f.Subtitle.Length > 0)
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
