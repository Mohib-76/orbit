using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Orbit.Core;
using Orbit.Core.Mirror;
using WKey = System.Windows.Input.Key;
using AKey = Orbit.Core.Mirror.Key;

namespace Orbit.UI;

/// <summary>The phone's screen, live, with a control toolbar and the stream panel (design: WinMirror).</summary>
public partial class MirrorWindow : Window
{
    OrbitCore Core => App.Core;
    MirrorState M => Core.Mirror;

    MirrorSession? _session;
    WriteableBitmap? _bitmap;
    bool _presentQueued, _binding, _touching, _shadeOpen;
    int _videoW = 1080, _videoH = 2340;
    readonly DispatcherTimer _flashTimer = new() { Interval = TimeSpan.FromSeconds(2.2) };

    public MirrorWindow()
    {
        InitializeComponent();
        Core.MirrorSessionStarted += Attach;
        M.PropertyChanged += OnMirrorChanged;
        Core.Phone.PropertyChanged += OnPhoneChanged;
        Core.MirrorStoppedByPhone += OnStoppedByPhone;
        _flashTimer.Tick += (_, _) => { _flashTimer.Stop(); Flash.Visibility = Visibility.Collapsed; Hints.Visibility = Visibility.Visible; };

        DragEnter += (_, e) =>
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            DropText.Text = $"Drop to send to {Core.Phone.Name}";
            DropOverlay.Visibility = Visibility.Visible;
        };
        DragLeave += (_, _) => DropOverlay.Visibility = Visibility.Collapsed;
        Drop += (_, e) =>
        {
            DropOverlay.Visibility = Visibility.Collapsed;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files) { Core.SendFiles(files); ShowFlash($"Sending {files.Length} file{(files.Length == 1 ? "" : "s")} to {Core.Phone.Name}"); }
        };

        LoadSettings();
        RefreshTitle();
        RefreshState();
        Loaded += (_, _) => { Stage.Focus(); Core.StartMirror(); };
    }

    // =====================================================================
    // Session and frames
    // =====================================================================

    void Attach(MirrorSession s)
    {
        if (Recorder.IsRecording) StopRecording(); // a recording belongs to one stream
        if (_session != null) { _session.FrameReady -= OnFrameReady; _session.VideoSizeChanged -= OnVideoSize; }
        _session = s;
        s.FrameReady += OnFrameReady;
        s.VideoSizeChanged += OnVideoSize;
        _touching = false;
    }

    void OnFrameReady()
    {
        if (_presentQueued) return;
        _presentQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, Present);
    }

    void Present()
    {
        _presentQueued = false;
        _session?.TakeFrame((pixels, w, h) =>
        {
            if (_bitmap == null || _bitmap.PixelWidth != w || _bitmap.PixelHeight != h)
            {
                _bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
                Video.Source = _bitmap;
            }
            _bitmap.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
        });
    }

    void OnVideoSize(int w, int h) => Dispatcher.BeginInvoke(() =>
    {
        bool rotated = (w > h) != (_videoW > _videoH);
        double oldW = Screen.Width, oldH = Screen.Height;
        _videoW = w; _videoH = h;
        if (Recorder.IsRecording) StopRecording(); // one MP4 track, one picture size
        if (rotated && Core.Settings.MirrorAutoResize && WindowState == WindowState.Normal) ResizeForRotation(oldW, oldH);
        LayoutScreen();
    });

    /// <summary>Keeps the phone's long edge the same length and fits the window around it.</summary>
    void ResizeForRotation(double oldW, double oldH)
    {
        double extraW = ActualWidth - oldW, extraH = ActualHeight - oldH;
        double longEdge = Math.Max(oldW, oldH);
        double sw = _videoW >= _videoH ? longEdge : longEdge * _videoW / _videoH;
        double sh = _videoW >= _videoH ? longEdge * _videoH / _videoW : longEdge;
        var area = SystemParameters.WorkArea;
        double fit = Math.Min(1, Math.Min((area.Width - extraW) / sw, (area.Height - extraH) / sh));
        Width = Math.Max(MinWidth, sw * fit + extraW);
        Height = Math.Max(MinHeight, sh * fit + extraH);
        Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
    }

    void Stage_SizeChanged(object s, SizeChangedEventArgs e) => LayoutScreen();

    /// <summary>Sizes the phone to the biggest screen that fits the stage at the video's aspect ratio.</summary>
    void LayoutScreen()
    {
        const double pad = 40, bezel = 18, hints = 34, toolbar = 70;
        double availW = Stage.ActualWidth - pad - bezel - toolbar, availH = Stage.ActualHeight - pad - bezel - hints;
        if (availW <= 0 || availH <= 0) return;
        double scale = Math.Min(availW / _videoW, availH / _videoH);
        Screen.Width = Math.Max(80, Math.Floor(_videoW * scale));
        Screen.Height = Math.Max(80, Math.Floor(_videoH * scale));
        double r = Math.Min(32, Math.Min(Screen.Width, Screen.Height) * 0.09);
        Bezel.CornerRadius = BezelShadow.CornerRadius = new CornerRadius(r + 8);
        Screen.Clip = new RectangleGeometry(new Rect(0, 0, Screen.Width, Screen.Height), r, r);
        Hints.MaxWidth = Flash.MaxWidth = Math.Max(Screen.Width + toolbar + 140, 300);
    }

    // =====================================================================
    // State
    // =====================================================================

    void OnMirrorChanged(object? s, PropertyChangedEventArgs e) => RefreshState();
    void OnPhoneChanged(object? s, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(PhoneState.Name)) RefreshTitle(); }

    void RefreshTitle()
    {
        var name = string.IsNullOrEmpty(Core.Phone.Name) ? "Phone" : Core.Phone.Name;
        Title = TitleText.Text = $"{name} — Orbit mirror";
    }

    void RefreshState()
    {
        var phase = M.Phase;
        ConnectingPanel.Visibility = phase == MirrorPhase.Connecting ? Visibility.Visible : Visibility.Collapsed;
        LostPanel.Visibility = phase == MirrorPhase.Lost ? Visibility.Visible : Visibility.Collapsed;
        StatsChip.Visibility = phase == MirrorPhase.Live && M.Stats.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        StatsText.Text = M.Stats;
        ViewOnlyChip.Visibility = M.ViewOnly ? Visibility.Visible : Visibility.Collapsed;
        Toolbar.IsEnabled = phase == MirrorPhase.Live;
        LostText.Text = M.Message;
        UseUsbBtn.Visibility = M.UsbAvailable && M.Transport != "USB" ? Visibility.Visible : Visibility.Collapsed;
        SetupBtn.Visibility = M.NeedsSetup ? Visibility.Visible : Visibility.Collapsed;
        Screen.Cursor = M.ViewOnly ? Cursors.No : Cursors.Arrow;

        if (phase == MirrorPhase.Connecting)
        {
            var via = M.Transport == "Wi-Fi" ? "wireless ADB" : "USB";
            RenderChecklist(new[]
            {
                (M.Step >= 1, M.Step == 0, M.Step >= 1 ? $"Connected over {via}" : "Reaching the phone over ADB"),
                (M.Step >= 2, M.Step == 1, "Mirror service started on phone"),
                (M.Step >= 3, M.Step == 2, "Waiting for first frame"),
            });
        }
        if (phase != MirrorPhase.Live && phase != MirrorPhase.Connecting && Recorder.IsRecording) StopRecording();
    }

    void RenderChecklist((bool done, bool active, string text)[] rows)
    {
        Checklist.Children.Clear();
        foreach (var (done, active, text) in rows)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            FrameworkElement mark = done
                ? new Ic { Data = (Geometry)FindResource("IcCheck"), Width = 14, Height = 14, Thickness = 3, Foreground = (Brush)FindResource("MirrorLive") }
                : new System.Windows.Shapes.Ellipse
                {
                    Width = 8, Height = 8, Margin = new Thickness(3, 0, 3, 0),
                    Fill = active ? (Brush)FindResource("MirrorPending") : (Brush)FindResource("Track")
                };
            mark.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(mark);
            row.Children.Add(new TextBlock
            {
                Text = text, FontSize = 13, Margin = new Thickness(8, 0, 0, 0),
                Foreground = (Brush)FindResource(done || active ? "Text" : "Text2")
            });
            Checklist.Children.Add(row);
        }
    }

    void OnStoppedByPhone()
    {
        Close();
        Toasts.Simple("Mirroring stopped", $"Stopped from {Core.Phone.Name}.");
    }

    void Reconnect_Click(object s, RoutedEventArgs e) => Core.StartMirror();
    void UseUsb_Click(object s, RoutedEventArgs e) => Core.StartMirror(preferUsb: true);
    void Setup_Click(object s, RoutedEventArgs e) { App.Current.OpenMirrorSetup(); Close(); }

    void ShowFlash(string text)
    {
        Flash.Text = text;
        Flash.Visibility = Visibility.Visible;
        Hints.Visibility = Visibility.Collapsed;
        _flashTimer.Stop();
        _flashTimer.Start();
    }

    // =====================================================================
    // Stream panel
    // =====================================================================

    void LoadSettings()
    {
        var st = Core.Settings;
        _binding = true;
        (st.MirrorQuality switch { "720p" => Q720, "1080p" => Q1080, "Max" => QMax, _ => QAuto }).IsChecked = true;
        (st.MirrorFps switch { 30 => F30, 90 => F90, _ => F60 }).IsChecked = true;
        OptAudio.IsChecked = st.MirrorAudio;
        OptScreenOff.IsChecked = ScreenOffBtn.IsChecked = st.MirrorScreenOff;
        OptAwake.IsChecked = st.MirrorKeepAwake;
        OptTouches.IsChecked = st.MirrorShowTouches;
        OptResize.IsChecked = st.MirrorAutoResize;
        PinToggle.IsChecked = Topmost = st.MirrorOnTop;
        PanelToggle.IsChecked = st.MirrorPanel;
        StreamPanel.Visibility = st.MirrorPanel ? Visibility.Visible : Visibility.Collapsed;
        _binding = false;
    }

    void Quality_Checked(object s, RoutedEventArgs e)
    {
        if (_binding || s is not RadioButton { Tag: string q } || q == Core.Settings.MirrorQuality) return;
        Core.Settings.MirrorQuality = q;
        Core.RestartMirror();
    }

    void Fps_Checked(object s, RoutedEventArgs e)
    {
        if (_binding || s is not RadioButton { Tag: string f } || int.Parse(f) == Core.Settings.MirrorFps) return;
        Core.Settings.MirrorFps = int.Parse(f);
        Core.RestartMirror();
    }

    void Opt_Changed(object s, RoutedEventArgs e)
    {
        if (_binding) return;
        var st = Core.Settings;
        bool on = ((CheckBox)s).IsChecked == true;
        if (s == OptScreenOff) { SetScreenOff(on); return; }
        if (s == OptResize) { st.MirrorAutoResize = on; st.Save(); return; }
        if (s == OptAudio) st.MirrorAudio = on;
        else if (s == OptAwake) st.MirrorKeepAwake = on;
        else if (s == OptTouches) st.MirrorShowTouches = on;
        Core.RestartMirror(); // these are fixed when the stream starts
    }

    void SetScreenOff(bool off)
    {
        _binding = true;
        OptScreenOff.IsChecked = ScreenOffBtn.IsChecked = off;
        _binding = false;
        Core.SetMirrorScreenOff(off);
    }

    void Pin_Changed(object s, RoutedEventArgs e)
    {
        if (_binding) return;
        Topmost = Core.Settings.MirrorOnTop = PinToggle.IsChecked == true;
        Core.Settings.Save();
    }

    void Panel_Changed(object s, RoutedEventArgs e)
    {
        if (_binding) return;
        bool show = PanelToggle.IsChecked == true;
        Core.Settings.MirrorPanel = show;
        Core.Settings.Save();
        // Grow or shrink the window so the phone keeps its size.
        if (WindowState == WindowState.Normal) Width = Math.Max(MinWidth, Width + (show ? StreamPanel.Width : -StreamPanel.Width));
        StreamPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    // =====================================================================
    // Toolbar and shortcuts
    // =====================================================================

    void Tool_Click(object s, RoutedEventArgs e)
    {
        if (s is FrameworkElement { Tag: string tag }) Run(tag);
        Stage.Focus();
    }

    bool CanControl => _session != null && M.Phase == MirrorPhase.Live && !M.ViewOnly;

    void Send(byte[] msg) { if (CanControl) _session!.Send(msg); }

    void PressKey(int keycode, int meta = 0)
    {
        Send(Msg.Keycode(Msg.ActionDown, keycode, meta));
        Send(Msg.Keycode(Msg.ActionUp, keycode, meta));
    }

    void Run(string action)
    {
        switch (action)
        {
            case "back": Send(Msg.BackOrScreenOn(Msg.ActionDown)); Send(Msg.BackOrScreenOn(Msg.ActionUp)); break;
            case "home": PressKey(AKey.Home); break;
            case "recents": PressKey(AKey.AppSwitch); break;
            case "volup": PressKey(AKey.VolumeUp); break;
            case "voldown": PressKey(AKey.VolumeDown); break;
            case "rotate": Send(Msg.RotateDevice()); break;
            case "power": PressKey(AKey.Power); break;
            case "notifications":
                Send(_shadeOpen ? Msg.CollapsePanels() : Msg.ExpandNotifications());
                _shadeOpen = !_shadeOpen;
                break;
            case "screenoff":
                if (M.ViewOnly) { ScreenOffBtn.IsChecked = Core.Settings.MirrorScreenOff; break; }
                SetScreenOff(!Core.Settings.MirrorScreenOff);
                break;
            case "screenshot": Screenshot(); break;
            case "record": ToggleRecording(); break;
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        var key = e.Key == WKey.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        bool ctrl = mods.HasFlag(ModifierKeys.Control), shift = mods.HasFlag(ModifierKeys.Shift), alt = mods.HasFlag(ModifierKeys.Alt);

        if (ctrl && !alt)
        {
            string? action = (key, shift) switch
            {
                (WKey.H, false) => "home", (WKey.S, false) => "recents", (WKey.R, false) => "rotate",
                (WKey.Up, _) => "volup", (WKey.Down, _) => "voldown", (WKey.O, false) => "screenoff",
                (WKey.N, false) => "notifications", (WKey.P, false) => "power",
                (WKey.S, true) => "screenshot", (WKey.R, true) => "record", _ => null
            };
            if (action != null) { if (!e.IsRepeat) Run(action); e.Handled = true; return; }
            if (key == WKey.V && !shift) { PasteToPhone(); e.Handled = true; return; }
        }
        if (key == WKey.Escape) { if (!e.IsRepeat) Run("back"); e.Handled = true; return; }
        if (StreamPanel.IsKeyboardFocusWithin) return;

        if (ctrl && !alt && key is >= WKey.A and <= WKey.Z)
        {
            // Ctrl+C/X/A/Z… go through as key combos so the phone's text fields handle them.
            Send(Msg.Keycode(Msg.ActionDown, AKey.A + (key - WKey.A), AKey.MetaCtrl | (shift ? AKey.MetaShift : 0), e.IsRepeat ? 1 : 0));
            Send(Msg.Keycode(Msg.ActionUp, AKey.A + (key - WKey.A), AKey.MetaCtrl | (shift ? AKey.MetaShift : 0)));
            e.Handled = true;
            return;
        }
        if (SpecialKey(key) is int code)
        {
            Send(Msg.Keycode(Msg.ActionDown, code, MetaOf(mods), e.IsRepeat ? 1 : 0));
            e.Handled = true;
        }
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (StreamPanel.IsKeyboardFocusWithin) return;
        var key = e.Key == WKey.System ? e.SystemKey : e.Key;
        if (SpecialKey(key) is int code) { Send(Msg.Keycode(Msg.ActionUp, code, MetaOf(Keyboard.Modifiers))); e.Handled = true; }
    }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        base.OnPreviewTextInput(e);
        if (StreamPanel.IsKeyboardFocusWithin || string.IsNullOrEmpty(e.Text) || e.Text.Any(char.IsControl)) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return;
        Send(Msg.Text(e.Text));
        e.Handled = true;
    }

    static int? SpecialKey(WKey k) => k switch
    {
        WKey.Enter => AKey.Enter, WKey.Back => AKey.Del, WKey.Delete => AKey.ForwardDel, WKey.Tab => AKey.Tab,
        WKey.Up => AKey.DpadUp, WKey.Down => AKey.DpadDown, WKey.Left => AKey.DpadLeft, WKey.Right => AKey.DpadRight,
        WKey.Home => AKey.MoveHome, WKey.End => AKey.MoveEnd, WKey.PageUp => AKey.PageUp, WKey.PageDown => AKey.PageDown,
        _ => null
    };

    static int MetaOf(ModifierKeys m) =>
        (m.HasFlag(ModifierKeys.Shift) ? AKey.MetaShift : 0) | (m.HasFlag(ModifierKeys.Control) ? AKey.MetaCtrl : 0) | (m.HasFlag(ModifierKeys.Alt) ? AKey.MetaAlt : 0);

    long _clipSeq;

    void PasteToPhone()
    {
        var text = ClipboardHost.TryGetText();
        if (string.IsNullOrEmpty(text) || !CanControl) return;
        _session!.Send(Msg.SetClipboard(++_clipSeq, text, paste: true));
    }

    void Screenshot()
    {
        if (_bitmap == null) return;
        var copy = _bitmap.Clone();
        copy.Freeze();
        try { Clipboard.SetImage(copy); ShowFlash("Screenshot copied to the clipboard"); }
        catch (Exception ex) { Log.Write("screenshot: " + ex.Message); }
    }

    // =====================================================================
    // Mouse → touch
    // =====================================================================

    (int x, int y)? Map(MouseEventArgs e)
    {
        var s = _session;
        if (s == null || s.VideoWidth == 0 || Video.ActualWidth <= 0) return null;
        var p = e.GetPosition(Video);
        int x = (int)Math.Clamp(p.X / Video.ActualWidth * s.VideoWidth, 0, s.VideoWidth - 1);
        int y = (int)Math.Clamp(p.Y / Video.ActualHeight * s.VideoHeight, 0, s.VideoHeight - 1);
        return (x, y);
    }

    void Touch(int action, MouseEventArgs e)
    {
        if (Map(e) is not var (x, y) || _session == null) return;
        Send(Msg.Touch(action, x, y, _session.VideoWidth, _session.VideoHeight, action == Msg.ActionUp ? 0f : 1f));
    }

    void Video_LeftDown(object s, MouseButtonEventArgs e)
    {
        Stage.Focus();
        if (!CanControl) return;
        _touching = Video.CaptureMouse();
        Touch(Msg.ActionDown, e);
        e.Handled = true;
    }

    void Video_Move(object s, MouseEventArgs e)
    {
        if (_touching && e.LeftButton == MouseButtonState.Pressed) Touch(Msg.ActionMove, e);
    }

    void Video_LeftUp(object s, MouseButtonEventArgs e)
    {
        if (!_touching) return;
        _touching = false;
        Touch(Msg.ActionUp, e);
        Video.ReleaseMouseCapture();
    }

    void Video_LostCapture(object s, MouseEventArgs e)
    {
        if (!_touching) return;
        _touching = false;
        Touch(Msg.ActionUp, e);
    }

    void Video_RightDown(object s, MouseButtonEventArgs e) { Send(Msg.BackOrScreenOn(Msg.ActionDown)); e.Handled = true; }
    void Video_RightUp(object s, MouseButtonEventArgs e) { Send(Msg.BackOrScreenOn(Msg.ActionUp)); e.Handled = true; }

    void Video_MouseDown(object s, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle) { PressKey(AKey.Home); e.Handled = true; }
    }

    void Video_Wheel(object s, MouseWheelEventArgs e)
    {
        if (Map(e) is not var (x, y) || _session == null) return;
        bool horizontal = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        float amount = e.Delta / 120f;
        Send(Msg.Scroll(x, y, _session.VideoWidth, _session.VideoHeight, horizontal ? amount : 0, horizontal ? 0 : amount));
        e.Handled = true;
    }

    // =====================================================================
    // Recording
    // =====================================================================

    MirrorRecorder Recorder { get; } = new();

    void ToggleRecording()
    {
        if (Recorder.IsRecording) StopRecording();
        else if (_session != null && M.Phase == MirrorPhase.Live)
        {
            var path = Recorder.Start(_session);
            if (path != null) ShowFlash("Recording…  Ctrl Shift R stops");
        }
        RecordBtn.IsChecked = Recorder.IsRecording;
        RecordIcon.IconFill = Recorder.IsRecording ? (Brush)FindResource("MirrorAlert") : null;
    }

    void StopRecording()
    {
        var path = Recorder.Stop();
        RecordBtn.IsChecked = false;
        RecordIcon.IconFill = null;
        if (path != null) Toasts.Saved("Recording saved", path);
    }

    // =====================================================================
    // Chrome
    // =====================================================================

    void Min_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Max_Click(object s, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object s, RoutedEventArgs e) => Close();

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (Recorder.IsRecording) StopRecording();
        Core.MirrorSessionStarted -= Attach;
        M.PropertyChanged -= OnMirrorChanged;
        Core.Phone.PropertyChanged -= OnPhoneChanged;
        Core.MirrorStoppedByPhone -= OnStoppedByPhone;
        Core.StopMirror();
        base.OnClosed(e);
    }
}
