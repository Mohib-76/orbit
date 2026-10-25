using System.Windows;
using System.Windows.Controls;
using Orbit.Core;

namespace Orbit.UI.Pages;

/// <summary>Settings › Phone remote: what the paired phone may do on this PC.</summary>
public partial class RemoteSettingsPage : UserControl
{
    OrbitCore Core => App.Core;
    bool _loading;
    RemoteCommand? _editing;

    sealed record CommandRow(RemoteCommand Cmd)
    {
        public string Name => Cmd.Name;
        public string Command => Cmd.Command;
        public string AskText => Cmd.Ask ? "Asks on phone" : "Runs right away";
        public string RemoveLabel => "Remove " + Cmd.Name;
    }

    public RemoteSettingsPage()
    {
        InitializeComponent();
        SizeChanged += (_, _) => { Grid2.Columns = ActualWidth < 900 ? 1 : 2; FormGrid.Columns = ActualWidth < 760 ? 1 : 2; };
        IsVisibleChanged += (_, _) => { if (IsVisible) Load(); };
        Load();
    }

    void Load()
    {
        _loading = true;
        var s = Core.Settings;
        var phone = string.IsNullOrEmpty(Core.Phone.Name) ? "your phone" : Core.Phone.Name;
        Intro.Text = $"Choose what {phone} can do on this PC. The phone only shows the buttons you allow here, and can't send anything else.";
        VolumeToggle.IsChecked = s.RemoteVolume;
        InputToggle.IsChecked = s.RemoteInput;
        SlidesToggle.IsChecked = s.RemoteSlides;
        CameraToggle.IsChecked = s.RemoteCamera;
        LockToggle.IsChecked = s.RemoteLock;
        SleepToggle.IsChecked = s.RemoteSleep;
        ShutdownToggle.IsChecked = s.RemoteShutdown;
        RestartToggle.IsChecked = s.RemoteRestart;
        LinksOpen.IsChecked = s.LinksFromPhone != "ask";
        LinksAsk.IsChecked = s.LinksFromPhone == "ask";
        var hk = string.IsNullOrEmpty(s.HotkeyLink) ? "" : $" To send one the other way, copy a link and press {s.HotkeyLink.Replace("+", " ")}.";
        LinksLine.Text = $"Shared on {phone} with Share › Open on {s.DeviceName}." + hk;
        ShowCommands();
        _loading = false;
    }

    void ShowCommands()
    {
        Commands.ItemsSource = Core.Settings.RemoteCommands.Select(c => new CommandRow(c)).ToList();
        NoCommands.Visibility = Core.Settings.RemoteCommands.Count == 0 && Form.Visibility != Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;
    }

    void Back_Click(object s, RoutedEventArgs e) => App.Current.ShowMain("settings");

    void Toggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var s = Core.Settings;
        s.RemoteVolume = VolumeToggle.IsChecked == true;
        s.RemoteInput = InputToggle.IsChecked == true;
        s.RemoteSlides = SlidesToggle.IsChecked == true;
        s.RemoteCamera = CameraToggle.IsChecked == true;
        s.RemoteLock = LockToggle.IsChecked == true;
        s.RemoteSleep = SleepToggle.IsChecked == true;
        s.RemoteShutdown = ShutdownToggle.IsChecked == true;
        s.RemoteRestart = RestartToggle.IsChecked == true;
        Core.SaveRemoteSettings();
    }

    void Links_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Core.Settings.LinksFromPhone = LinksAsk.IsChecked == true ? "ask" : "open";
        Core.SaveRemoteSettings();
    }

    // ---------------- commands

    void OpenForm(RemoteCommand? cmd)
    {
        _editing = cmd;
        NameBox.Text = cmd?.Name ?? "";
        CommandBox.Text = cmd?.Command ?? "";
        AskBox.IsChecked = cmd?.Ask ?? false;
        Form.Visibility = Visibility.Visible;
        AddBtn.IsEnabled = false;
        ShowCommands();
        Form_Changed(this, null);
        NameBox.Focus();
    }

    void CloseForm()
    {
        _editing = null;
        Form.Visibility = Visibility.Collapsed;
        AddBtn.IsEnabled = true;
        ShowCommands();
    }

    void Add_Click(object s, RoutedEventArgs e) => OpenForm(null);
    void Edit_Click(object s, RoutedEventArgs e) { if (s is FrameworkElement { Tag: CommandRow r }) OpenForm(r.Cmd); }
    void Cancel_Click(object s, RoutedEventArgs e) => CloseForm();

    void Form_Changed(object s, TextChangedEventArgs? e) =>
        SaveBtn.IsEnabled = NameBox.Text.Trim().Length > 0 && CommandBox.Text.Trim().Length > 0;

    void Browse_Click(object s, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Pick a program or script",
            Filter = "Programs and scripts|*.exe;*.bat;*.cmd;*.ps1;*.ahk;*.lnk;*.vbs|All files|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        CommandBox.Text = dlg.FileName;
        if (NameBox.Text.Trim().Length == 0) NameBox.Text = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
    }

    void SaveCommand_Click(object s, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var command = CommandBox.Text.Trim();
        if (name.Length == 0 || command.Length == 0) return;
        var cmd = _editing ?? new RemoteCommand();
        cmd.Name = name;
        cmd.Command = command;
        cmd.Ask = AskBox.IsChecked == true;
        if (_editing == null) Core.Settings.RemoteCommands = [.. Core.Settings.RemoteCommands, cmd];
        Core.SaveRemoteSettings();
        CloseForm();
    }

    void Remove_Click(object s, RoutedEventArgs e)
    {
        if (s is not FrameworkElement { Tag: CommandRow r }) return;
        if (MessageBox.Show($"Remove “{r.Name}”? It disappears from the phone too.", "Orbit",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        Core.Settings.RemoteCommands = Core.Settings.RemoteCommands.Where(c => c != r.Cmd).ToList();
        if (_editing == r.Cmd) CloseForm();
        Core.SaveRemoteSettings();
        ShowCommands();
    }
}
