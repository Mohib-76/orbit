using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Win32;
using QRCoder;

namespace Orbit.Core;

public static class Qr
{
    public static BitmapSource Render(string text)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(10, new byte[] { 0x16, 0x16, 0x1F }, new byte[] { 0xFF, 0xFF, 0xFF }, true);
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.StreamSource = new MemoryStream(png);
        img.EndInit();
        img.Freeze();
        return img;
    }
}

/// <summary>Windows toast notifications.</summary>
public static class Toasts
{
    const string Group = "orbit";

    public static void Init(Action<ToastArguments> onActivated)
    {
        ToastNotificationManagerCompat.OnActivated += e =>
        {
            var args = ToastArguments.Parse(e.Argument);
            Ui.Run(() => onActivated(args));
        };
    }

    static string Tag(string key) => Convert.ToHexString(
        System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..32];

    public static void Show(PhoneNotification n, string phoneName, bool hideContent = false)
    {
        try
        {
            var b = new ToastContentBuilder()
                .AddArgument("action", "open").AddArgument("page", "notifications")
                .AddText(hideContent ? n.App : string.IsNullOrEmpty(n.Title) ? n.App : n.Title)
                .AddText(hideContent ? "New notification" : n.Text)
                .AddAttributionText($"{n.App} · {phoneName}")
                .AddButton(new ToastButton().SetContent("Dismiss on phone")
                    .AddArgument("action", "dismiss").AddArgument("key", n.Key).SetBackgroundActivation());
            if (n.IconPath != null && File.Exists(n.IconPath))
                b.AddAppLogoOverride(new Uri(n.IconPath), ToastGenericAppLogoCrop.Default);
            b.Show(t => { t.Tag = Tag(n.Key); t.Group = Group; });
        }
        catch (Exception ex) { Log.Write("toast: " + ex.Message); }
    }

    public static void Remove(string key)
    {
        try { ToastNotificationManagerCompat.History.Remove(Tag(key), Group); } catch { }
    }

    public static void ClearAll()
    {
        try { ToastNotificationManagerCompat.History.Clear(); } catch { }
    }

    public static void Simple(string title, string text)
    {
        try { new ToastContentBuilder().AddArgument("action", "open").AddText(title).AddText(text).Show(); }
        catch (Exception ex) { Log.Write("toast: " + ex.Message); }
    }

    public static void LinkOpened(string phone, string url)
    {
        try { new ToastContentBuilder().AddArgument("action", "openurl").AddArgument("url", url)
                .AddText($"Opened a link from {phone}").AddText(Links.Short(url)).Show(); }
        catch (Exception ex) { Log.Write("toast: " + ex.Message); }
    }

    /// <summary>Settings › Phone remote › "Ask first": the link opens only from the toast.</summary>
    public static void LinkAsk(string phone, string url)
    {
        try
        {
            new ToastContentBuilder()
                .AddArgument("action", "openurl").AddArgument("url", url)
                .AddText($"{phone} sent a link").AddText(Links.Short(url))
                .AddButton(new ToastButton().SetContent("Open").AddArgument("action", "openurl").AddArgument("url", url))
                .AddButton(new ToastButton().SetContent("Copy").AddArgument("action", "copyurl").AddArgument("url", url))
                .Show();
        }
        catch (Exception ex) { Log.Write("toast: " + ex.Message); }
    }

    /// <summary>Every remote action (power buttons, commands) says so, so nothing runs silently.</summary>
    public static void RemoteAction(string title, string phone)
    {
        try
        {
            new ToastContentBuilder()
                .AddArgument("action", "open").AddArgument("page", "remote")
                .AddText(title).AddText($"From {phone} · just now")
                .AddButton(new ToastButton().SetContent("Settings").AddArgument("action", "open").AddArgument("page", "remote"))
                .Show();
        }
        catch (Exception ex) { Log.Write("toast: " + ex.Message); }
    }

    public static void LowBattery(string phone, int level) =>
        Simple($"{phone} battery is at {level}%", "Plug it in soon.");

    public static void FileSharingOn(string phone)
    {
        try
        {
            new ToastContentBuilder()
                .AddArgument("action", "openphone")
                .AddText("File sharing on")
                .AddText($"{phone} is in File Explorer.")
                .AddButton(new ToastButton().SetContent("Open").AddArgument("action", "openphone"))
                .Show(t => t.Tag = "usb");
        }
        catch (Exception ex) { Log.Write("toast: " + ex.Message); }
    }

    public static void UsbSwitchFailed()
    {
        try
        {
            new ToastContentBuilder()
                .AddArgument("action", "usbhelp")
                .AddText("Couldn't switch USB mode")
                .AddText("Turn on USB debugging on your phone.")
                .AddButton(new ToastButton().SetContent("How").AddArgument("action", "usbhelp"))
                .Show(t => t.Tag = "usb");
        }
        catch (Exception ex) { Log.Write("toast: " + ex.Message); }
    }

    public static void FileOffer(Transfer t, string phone)
    {
        try
        {
            new ToastContentBuilder()
                .AddArgument("action", "open").AddArgument("page", "files")
                .AddText($"{phone} wants to send a file")
                .AddText($"{t.Name} ({Transfer.FormatSize(t.Size)})")
                .AddButton(new ToastButton().SetContent("Accept").AddArgument("action", "accept").AddArgument("id", t.Id).SetBackgroundActivation())
                .AddButton(new ToastButton().SetContent("Decline").AddArgument("action", "decline").AddArgument("id", t.Id).SetBackgroundActivation())
                .Show();
        }
        catch (Exception ex) { Log.Write("toast: " + ex.Message); }
    }

    public static void Saved(string title, string path)
    {
        try
        {
            new ToastContentBuilder()
                .AddArgument("action", "openfile").AddArgument("path", path)
                .AddText(title)
                .AddText(Path.GetFileName(path))
                .AddButton(new ToastButton().SetContent("Open").AddArgument("action", "openfile").AddArgument("path", path))
                .AddButton(new ToastButton().SetContent("Show in folder").AddArgument("action", "showfile").AddArgument("path", path))
                .Show();
        }
        catch (Exception ex) { Log.Write("toast: " + ex.Message); }
    }

    public static void FileReceived(Transfer t, string phone)
    {
        try
        {
            new ToastContentBuilder()
                .AddArgument("action", "openfile").AddArgument("path", t.LocalPath)
                .AddText($"Received {t.Name}")
                .AddText($"From {phone} · {Transfer.FormatSize(t.Size)}")
                .AddButton(new ToastButton().SetContent("Open").AddArgument("action", "openfile").AddArgument("path", t.LocalPath))
                .AddButton(new ToastButton().SetContent("Show in folder").AddArgument("action", "showfile").AddArgument("path", t.LocalPath))
                .Show();
        }
        catch (Exception ex) { Log.Write("toast: " + ex.Message); }
    }
}

/// <summary>
/// A hidden message-only window that listens for clipboard changes and the global hotkey.
/// </summary>
public static class ClipboardHost
{
    const int WM_CLIPBOARDUPDATE = 0x031D, WM_HOTKEY = 0x0312;
    const int HOTKEY_SEND_CLIPBOARD = 1, HOTKEY_CYCLE_ANC = 2, HOTKEY_MIRROR = 3, HOTKEY_MEDIA = 4, HOTKEY_LINK = 5;
    /// <summary>Clips bigger than this aren't synced automatically.</summary>
    public const int MaxClipChars = 1_000_000;

    [DllImport("user32.dll", SetLastError = true)] static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll", SetLastError = true)] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    static HwndSource? _src;
    static bool _settingText;

    /// <param name="onCopied">Text and whether the copying app marked it private (password managers).</param>
    public static void Start(Action<string, bool> onCopied, Action onHotkey, Action onCycleAnc, Action onMirror, Action onMedia, Action onLink)
    {
        var p = new HwndSourceParameters("OrbitClipboard") { ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */, Width = 0, Height = 0 };
        _src = new HwndSource(p);
        _src.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg == WM_CLIPBOARDUPDATE && !_settingText)
            {
                var text = TryGetText();
                // "" for images and files, so a remembered link goes away.
                onCopied(text ?? "", text != null && IsSensitive());
            }
            else if (msg == WM_HOTKEY && w.ToInt32() == HOTKEY_SEND_CLIPBOARD) onHotkey();
            else if (msg == WM_HOTKEY && w.ToInt32() == HOTKEY_CYCLE_ANC) onCycleAnc();
            else if (msg == WM_HOTKEY && w.ToInt32() == HOTKEY_MIRROR) onMirror();
            else if (msg == WM_HOTKEY && w.ToInt32() == HOTKEY_MEDIA) onMedia();
            else if (msg == WM_HOTKEY && w.ToInt32() == HOTKEY_LINK) onLink();
            return IntPtr.Zero;
        });
        AddClipboardFormatListener(_src.Handle);
    }

    /// <summary>(Re)registers the hotkeys. Returns the ones Windows refused (already used by another app).</summary>
    public static List<string> ApplyHotkeys(string clipboard, string noise, string mirror, string media, string link)
    {
        var failed = new List<string>();
        if (_src == null) return failed;
        foreach (var (id, text) in new[] { (HOTKEY_SEND_CLIPBOARD, clipboard), (HOTKEY_CYCLE_ANC, noise), (HOTKEY_MIRROR, mirror), (HOTKEY_MEDIA, media), (HOTKEY_LINK, link) })
        {
            UnregisterHotKey(_src.Handle, id);
            if (Hotkey.Parse(text) is not { } hk) continue;
            if (!RegisterHotKey(_src.Handle, id, hk.Win32Modifiers, hk.VirtualKey)) failed.Add(hk.ToString());
        }
        if (failed.Count > 0) Log.Write("hotkeys in use by another app: " + string.Join(", ", failed));
        return failed;
    }

    /// <summary>
    /// True when the copying app asked clipboard managers to skip this clip: the
    /// "ExcludeClipboardContentFromMonitorProcessing" format, or "CanIncludeInClipboardHistory" = 0.
    /// </summary>
    static bool IsSensitive()
    {
        try
        {
            if (Clipboard.ContainsData("ExcludeClipboardContentFromMonitorProcessing")) return true;
            if (Clipboard.GetData("CanIncludeInClipboardHistory") is MemoryStream ms && ms.Length >= 4)
                return BitConverter.ToInt32(ms.ToArray(), 0) == 0;
        }
        catch { }
        return false;
    }

    public static string? TryGetText()
    {
        for (int i = 0; i < 5; i++)
        {
            try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
            catch (COMException) { Thread.Sleep(30); }
        }
        return null;
    }

    public static void SetText(string text)
    {
        _settingText = true;
        try
        {
            for (int i = 0; i < 5; i++)
            {
                try { Clipboard.SetDataObject(text, true); return; }
                catch (COMException) { Thread.Sleep(30); }
            }
        }
        finally
        {
            // Swallow the WM_CLIPBOARDUPDATE our own write triggers.
            Application.Current.Dispatcher.BeginInvoke(() => _settingText = false,
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }
}

public static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Apply(bool enabled)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (k == null) return;
            if (enabled) k.SetValue("Orbit", $"\"{Environment.ProcessPath}\" --tray");
            else k.DeleteValue("Orbit", false);
        }
        catch (Exception ex) { Log.Write("autostart: " + ex.Message); }
    }
}

public static class SystemTheme
{
    public static bool IsDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }
}
