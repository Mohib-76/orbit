using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Orbit.Core;

namespace Orbit.UI.Pages;

public partial class SettingsPage : UserControl
{
    OrbitCore Core => App.Core;
    bool _loading = true;

    public SettingsPage()
    {
        InitializeComponent();
        PcName.Text = Core.Settings.DeviceName;
        ThemeBox.SelectedIndex = Core.Settings.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        VersionText.Text = $"Orbit {Proto.AppVersion} for Windows · protocol {Proto.Version}";
        ManualAddressBox.Text = Core.Settings.ManualAddress ?? "";
        BatteryBox.SelectedItem = BatteryBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == Core.Settings.LowBatteryThreshold.ToString())
                                  ?? BatteryBox.Items[2];
        ShowHotkeys();
        ShowUsbDefault();
        ShowMedia();
        Core.UsbChanged += () => { if (IsVisible) ShowUsbDefault(); };
        Core.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(OrbitCore.VersionWarning)) ShowVersionWarning(); };
        ShowVersionWarning();
        SizeChanged += (_, _) => { Grid2.Columns = ActualWidth < 900 ? 1 : 2; UsbChoices.Columns = ActualWidth < 820 ? 1 : 3; };
        Core.Phone.PropertyChanged += (_, _) => Refresh();
        Core.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OrbitCore.DoNotDisturb)) Refresh();
            if (e.PropertyName == nameof(OrbitCore.Settings))
            {
                // Settings can change from the phone (auto tethering); re-read the bindings.
                var dc = DataContext; DataContext = null; DataContext = dc;
            }
        };
        IsVisibleChanged += (_, _) => { if (IsVisible) { Refresh(); RefreshBuds(); ShowUsbDefault(); ShowMedia(); } };
        Core.BudsChanged += () => { if (IsVisible) BudsPickStateOnly(); };
        Refresh();
        RefreshBuds();
        _loading = false;
    }

    void Refresh()
    {
        var p = Core.Settings.Phone;
        PhoneName.Text = p?.Name ?? "No phone paired";
        PhoneMeta.Text = p == null ? "Pair a phone to get started"
            : $"{(Core.Phone.IsConnected ? "Connected now" : "Not connected")} · {p.Model} · paired {p.PairedAt:d MMM}";
        LinkNow.Text = Core.Phone.IsConnected ? Core.Phone.Link : "–";
        FolderText.Text = Core.Settings.SaveFolder;
        DndBtn.Content = Core.DoNotDisturb ? "Resume" : "Pause 1 h";
        bool paired = Core.Settings.MirrorWirelessPaired;
        MirrorSetupBtn.Content = paired ? "Pair again" : "Set up";
        MirrorWirelessLine.Text = paired
            ? "Set up. Mirroring connects over Wi-Fi when the phone isn't plugged in (same Wi-Fi as this PC)."
            : "Mirror without a cable over Wi-Fi. One-time setup with a QR code, about 30 seconds.";
    }

    void Mirror_Click(object s, RoutedEventArgs e) => App.Current.OpenMirror();
    void MirrorSetup_Click(object s, RoutedEventArgs e) => App.Current.OpenMirrorSetup();

    void Save(object s, RoutedEventArgs e) { Core.Settings.Save(); Core.SendSettings(); }

    // ---------------- earbuds

    sealed record BudsChoice(string Mac, string Name, bool Connected)
    {
        public string Label => Name + (Connected ? "  · connected" : "");
    }

    bool _pickerLoading;

    void BudsPickStateOnly()
    {
        _settingToggle = true;
        BudsToggle.IsChecked = Core.BudsEnabled;
        _settingToggle = false;
        BudsPickRow.Visibility = Core.BudsEnabled ? Visibility.Visible : Visibility.Collapsed;
        BudsPickState.Text = Core.Settings.BudsMac == null ? "Pick the buds paired with this PC"
            : Core.BudsLocal.State.Connected ? "Connected to this PC"
            : Core.BudsViaPhone ? "Controlled through your phone"
            : "Not connected right now";
    }

    async void RefreshBuds()
    {
        BudsPickStateOnly();
        if (!Core.BudsEnabled || _pickerLoading || BudsPicker.IsDropDownOpen) return;
        _pickerLoading = true;
        try
        {
            var list = (await Orbit.Core.Buds.BudsController.PairedDevicesAsync()).Select(d => new BudsChoice(d.mac, d.name, d.connected)).ToList();
            BudsPicker.ItemsSource = list;
            BudsPicker.SelectedItem = list.FirstOrDefault(d => d.Mac.Equals(Core.Settings.BudsMac ?? "", StringComparison.OrdinalIgnoreCase));
        }
        finally { _pickerLoading = false; }
    }

    bool _settingToggle;

    void BudsToggle_Click(object s, RoutedEventArgs e)
    {
        if (_settingToggle || (BudsToggle.IsChecked == true) == Core.BudsEnabled) return;
        Core.SetBudsEnabled(BudsToggle.IsChecked == true);
        RefreshBuds();
    }

    void BudsPicker_Changed(object s, SelectionChangedEventArgs e)
    {
        if (_pickerLoading || BudsPicker.SelectedItem is not BudsChoice c) return;
        if (c.Mac.Equals(Core.Settings.BudsMac ?? "", StringComparison.OrdinalIgnoreCase)) return;
        Core.SelectBuds(c.Mac, c.Name);
    }

    // ---------------- USB cable default, media

    bool _usbLoading;

    void ShowUsbDefault()
    {
        _usbLoading = true;
        UsbCharging.IsChecked = Core.Settings.UsbMode == UsbModes.Charging;
        UsbTethering.IsChecked = Core.Settings.UsbMode == UsbModes.Tethering;
        UsbFiles.IsChecked = Core.Settings.UsbMode == UsbModes.Files;
        _usbLoading = false;
        var phone = Core.Settings.Phone?.Name;
        UsbSyncedLine.Text = (phone != null ? $"Synced with {phone}. " : "") + "You can switch any time from Home or the tray.";
    }

    void UsbDefault_Checked(object s, RoutedEventArgs e)
    {
        if (_usbLoading || s is not FrameworkElement { Tag: string mode } || mode == Core.Settings.UsbMode) return;
        Core.SetUsbDefault(mode);
    }

    void UsbHelp_Click(object s, RoutedEventArgs e) => App.OpenUrl(AppInfo.UsbDebuggingHelpUrl);

    bool _mediaLoading;

    void ShowMedia()
    {
        _mediaLoading = true;
        MediaShowToggle.IsChecked = Core.Settings.MediaShowPhone;
        MediaKeysToggle.IsChecked = Core.Settings.MediaKeys;
        MediaKeysToggle.IsEnabled = Core.Settings.MediaShowPhone;
        _mediaLoading = false;
    }

    void MediaShow_Changed(object s, RoutedEventArgs e)
    {
        if (_mediaLoading) return;
        Core.SetMediaShowPhone(MediaShowToggle.IsChecked == true);
        ShowMedia();
    }

    void MediaKeys_Changed(object s, RoutedEventArgs e)
    {
        if (_mediaLoading) return;
        Core.SetMediaKeys(MediaKeysToggle.IsChecked == true);
    }

    void Startup_Click(object s, RoutedEventArgs e) { Save(s, e); AutoStart.Apply(Core.Settings.StartWithWindows); }

    void Theme_Changed(object s, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        Core.Settings.Theme = ThemeBox.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "System" };
        Core.Settings.Save();
        App.Current.ApplyTheme(Core.Settings.Theme);
    }

    void PcName_LostFocus(object s, RoutedEventArgs e)
    {
        var name = PcName.Text.Trim();
        if (name.Length == 0 || name == Core.Settings.DeviceName) return;
        Core.Settings.DeviceName = name;
        Save(s, e);
    }

    void Rename_Click(object s, RoutedEventArgs e)
    {
        if (RenameBox.Visibility != Visibility.Visible)
        {
            RenameBox.Text = Core.Settings.Phone?.Name ?? "";
            RenameBox.Visibility = Visibility.Visible;
            RenameBtn.Content = "Save";
            RenameBox.Focus(); RenameBox.SelectAll();
            return;
        }
        Core.RenamePhone(RenameBox.Text);
        RenameBox.Visibility = Visibility.Collapsed;
        RenameBtn.Content = "Rename";
        Refresh();
    }

    void RenameBox_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Rename_Click(s, e);
        if (e.Key == Key.Escape) { RenameBox.Visibility = Visibility.Collapsed; RenameBtn.Content = "Rename"; }
    }

    void Unpair_Click(object s, RoutedEventArgs e)
    {
        if (Core.Settings.Phone == null) return;
        if (MessageBox.Show($"Unpair {Core.Settings.Phone.Name}? You'll need to pair again to reconnect.", "Orbit",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        Core.Unpair();
        App.Current.ShowMain("home");
    }

    void PairNew_Click(object s, RoutedEventArgs e) => Unpair_Click(s, e);

    void Dnd_Click(object s, RoutedEventArgs e) => Core.PauseNotifications(Core.DoNotDisturb ? null : TimeSpan.FromHours(1));

    void ChangeFolder_Click(object s, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = Core.Settings.SaveFolder, Title = "Save received files to" };
        if (dlg.ShowDialog() != true) return;
        Core.Settings.SaveFolder = dlg.FolderName;
        Core.Settings.Save();
        Refresh();
    }

    void Log_Click(object s, RoutedEventArgs e) => Process.Start(new ProcessStartInfo(Paths.LogFile) { UseShellExecute = true });

    // ---------------- connection, notifications

    void ManualAddress_LostFocus(object s, RoutedEventArgs e)
    {
        var text = ManualAddressBox.Text.Trim();
        if (text.Length > 0 && OrbitCore.ParseManualAddress(text) == null)
        {
            ManualAddressHint.Text = "That doesn't look like an address. Example: 192.168.1.20";
            return;
        }
        ManualAddressHint.Text = "If Orbit can't find your phone (VPN, guest Wi-Fi), enter the IP shown in Orbit on the phone.";
        Core.Settings.ManualAddress = text.Length == 0 ? null : text;
        Core.Settings.Save();
    }

    void Battery_Changed(object s, SelectionChangedEventArgs e)
    {
        if (_loading || BatteryBox.SelectedItem is not ComboBoxItem { Tag: string t }) return;
        Core.Settings.LowBatteryThreshold = int.Parse(t);
        Core.Settings.Save();
    }

    void ShowVersionWarning()
    {
        VersionWarningText.Text = Core.VersionWarning;
        VersionWarningText.Visibility = Core.VersionWarning.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------- keyboard shortcuts

    TextBox BoxFor(string which) => which switch { "noise" => HkNoise, "mirror" => HkMirror, "media" => HkMedia, _ => HkClipboard };

    void ShowHotkeys()
    {
        HkClipboard.Text = Show(Core.Settings.HotkeyClipboard);
        HkNoise.Text = Show(Core.Settings.HotkeyNoise);
        HkMirror.Text = Show(Core.Settings.HotkeyMirror);
        HkMedia.Text = Show(Core.Settings.HotkeyMedia);
        static string Show(string s) => string.IsNullOrEmpty(s) ? "Off" : s;
    }

    void SetHotkey(string which, string value)
    {
        switch (which)
        {
            case "noise": Core.Settings.HotkeyNoise = value; break;
            case "mirror": Core.Settings.HotkeyMirror = value; break;
            case "media": Core.Settings.HotkeyMedia = value; break;
            default: Core.Settings.HotkeyClipboard = value; break;
        }
        Core.Settings.Save();
        var failed = App.Current.ApplyHotkeys();
        ShowHotkeys();
        HotkeyHint.Text = failed.Count > 0
            ? $"{string.Join(", ", failed)} is already used by another app. Pick different keys."
            : "Click a box and press the new keys (with Ctrl, Alt or Win).";
    }

    void Hotkey_KeyDown(object s, KeyEventArgs e)
    {
        if (s is not TextBox { Tag: string which }) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab) return; // keep keyboard navigation working
        e.Handled = true;
        if (key is Key.Escape) { ShowHotkeys(); return; }
        if (key is Key.Back or Key.Delete) { SetHotkey(which, ""); return; }
        if (Hotkey.IsModifier(key)) return;
        var hk = new Hotkey(Keyboard.Modifiers, key);
        if ((hk.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0)
        {
            HotkeyHint.Text = "Include Ctrl, Alt or Win so the shortcut doesn't clash with typing.";
            return;
        }
        // Keep shortcuts unique within Orbit.
        var text = hk.ToString();
        foreach (var other in new[] { "clipboard", "noise", "mirror", "media" })
            if (other != which && BoxFor(other).Text == text) SetHotkey(other, "");
        SetHotkey(which, text);
    }

    void HotkeyClear_Click(object s, RoutedEventArgs e)
    {
        if (s is FrameworkElement { Tag: string which }) SetHotkey(which, "");
    }

    // ---------------- about, diagnostics, reset

    void About_Click(object s, RoutedEventArgs e) => new AboutWindow { Owner = Window.GetWindow(this) }.ShowDialog();

    void Diagnostics_Click(object s, RoutedEventArgs e)
    {
        try
        {
            var path = Diagnostics.Export(Core);
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (Exception ex) { MessageBox.Show("Couldn't export diagnostics: " + ex.Message, "Orbit"); }
    }

    void Reset_Click(object s, RoutedEventArgs e)
    {
        if (MessageBox.Show("Reset Orbit on this PC? This forgets your paired phone, this PC's keys and all settings, then restarts Orbit. Files you received stay where they are.",
                "Reset Orbit", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        Diagnostics.ResetAndRestart();
    }
    void Quit_Click(object s, RoutedEventArgs e) => App.Current.Quit();
}
