using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Toolkit.Uwp.Notifications;
using Orbit.Core;
using Orbit.Core.Buds;
using Orbit.UI;

namespace Orbit;

public partial class App : Application
{
    const string MutexName = "Orbit.SingleInstance.v1";
    const string ShowEventName = "Orbit.ShowWindow.v1";

    Mutex? _mutex;
    EventWaitHandle? _showEvent;
    MainWindow? _main;
    TrayIcon? _tray;

    public static OrbitCore Core => OrbitCore.Instance;
    public static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Toast button clicks may relaunch us with toast arguments; let the running copy handle them.
        _mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            Shutdown();
            return;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        new Thread(() => { while (_showEvent.WaitOne()) Dispatcher.BeginInvoke(() => ShowMain()); }) { IsBackground = true }.Start();

        base.OnStartup(e);
        string? lastUiError = null;
        DispatcherUnhandledException += (_, ex) =>
        {
            // Layout errors repeat on every frame; log each distinct one once.
            var msg = ex.Exception.Message;
            if (msg != lastUiError) Log.Write("UI error: " + ex.Exception);
            lastUiError = msg;
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log.Write("fatal: " + ex.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, ex) => { Log.Write("task: " + ex.Exception.Message); ex.SetObserved(); };

        ApplyTheme(Core.Settings.Theme);
        AutoStart.Apply(Core.Settings.StartWithWindows);
        Toasts.Init(OnToast);
        ClipboardHost.Start((text, sensitive) => Core.OnLocalClipboard(text, sensitive), SendClipboardNow, CycleNoiseMode, () => OpenMirror());
        ApplyHotkeys();
        Core.MirrorRequested += () => OpenMirror();
        Core.Start();

        _tray = new TrayIcon();
        bool launchedFromToast = ToastNotificationManagerCompat.WasCurrentProcessToastActivated();
        if (!e.Args.Contains("--tray") && !launchedFromToast) ShowMain();
    }

    /// <summary>Registers the hotkeys from Settings; returns the ones another app already uses.</summary>
    public List<string> ApplyHotkeys() =>
        ClipboardHost.ApplyHotkeys(Core.Settings.HotkeyClipboard, Core.Settings.HotkeyNoise, Core.Settings.HotkeyMirror);

    protected override void OnExit(ExitEventArgs e)
    {
        Core.Stop();
        _tray?.Dispose();
        base.OnExit(e);
    }

    public void ShowMain(string? page = null)
    {
        _main ??= new MainWindow();
        if (page != null) _main.Navigate(page);
        if (!_main.IsVisible) _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    MirrorWindow? _mirror;
    MirrorSetupWindow? _mirrorSetup;

    /// <summary>
    /// Opens the mirror window (Home button, tray, Ctrl+Alt+M, the phone's tile), or the one-time
    /// wireless setup when there's no USB phone and wireless mirroring was never paired.
    /// </summary>
    public void OpenMirror(bool skipSetup = false)
    {
        if (Core.Settings.Phone == null) { ShowMain(); return; }
        if (_mirror == null && !skipSetup && !Core.Settings.MirrorWirelessPaired && !Core.HasUsbMirrorDevice)
        {
            OpenMirrorSetup();
            return;
        }
        if (_mirror == null)
        {
            _mirror = new MirrorWindow();
            _mirror.Closed += (_, _) => _mirror = null;
            _mirror.Show();
        }
        if (_mirror.WindowState == WindowState.Minimized) _mirror.WindowState = WindowState.Normal;
        _mirror.Activate();
    }

    public void OpenMirrorSetup()
    {
        if (_mirrorSetup == null)
        {
            _mirrorSetup = new MirrorSetupWindow();
            _mirrorSetup.Closed += (_, _) => _mirrorSetup = null;
            _mirrorSetup.Show();
        }
        if (_mirrorSetup.WindowState == WindowState.Minimized) _mirrorSetup.WindowState = WindowState.Normal;
        _mirrorSetup.Activate();
    }

    public void Quit()
    {
        _tray?.Dispose();
        Shutdown();
    }

    public void SendClipboardNow()
    {
        var text = ClipboardHost.TryGetText();
        if (string.IsNullOrEmpty(text)) { _tray?.Balloon("Nothing to send", "The clipboard has no text."); return; }
        if (!Core.SendClipboardText(text)) _tray?.Balloon("Phone not connected", "Orbit will connect when your phone is nearby.");
    }

    public void CycleNoiseMode()
    {
        if (!Core.BudsEnabled) return;
        if (!Core.Buds.Connected) { _tray?.Balloon("Earbuds not connected", "Connect your buds to this PC or your phone."); return; }
        Core.BudsCycleAnc();
        _tray?.Balloon("Noise control", Core.Buds.NoiseLabel ?? "Not available on these buds");
    }

    public void PickAndSendFiles()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Send files to your phone" };
        if (dlg.ShowDialog() == true) Core.SendFiles(dlg.FileNames);
    }

    void OnToast(ToastArguments a)
    {
        a.TryGetValue("action", out string? action);
        switch (action)
        {
            case "dismiss" when a.TryGetValue("key", out string? key): Core.DismissNotification(key); break;
            case "accept" when a.TryGetValue("id", out string? id):
                if (Core.Transfers.FirstOrDefault(t => t.Id == id) is { } ta) Core.Accept(ta);
                break;
            case "decline" when a.TryGetValue("id", out string? id2):
                if (Core.Transfers.FirstOrDefault(t => t.Id == id2) is { } td) Core.Decline(td);
                break;
            case "openfile" when a.TryGetValue("path", out string? p):
                if (File.Exists(p)) Process.Start(new ProcessStartInfo(p) { UseShellExecute = true });
                break;
            case "showfile" when a.TryGetValue("path", out string? p2):
                Process.Start("explorer.exe", $"/select,\"{p2}\"");
                break;
            default:
                a.TryGetValue("page", out string? page);
                ShowMain(page);
                break;
        }
    }

    public void ApplyTheme(string theme)
    {
        bool dark = theme == "Dark" || (theme == "System" && SystemTheme.IsDark());
        var dicts = Resources.MergedDictionaries;
        dicts[0] = new ResourceDictionary { Source = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative) };
        IsDark = dark;
        ThemeChanged?.Invoke();
    }

    public bool IsDark { get; private set; }
    public event Action? ThemeChanged;
}
