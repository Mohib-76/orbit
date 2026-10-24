using System.Text.Json.Nodes;

namespace Orbit.Core;

/// <summary>
/// The phone's touchpad, keyboard and slides remote: <c>orbit.input</c> and <c>orbit.slides</c> become SendInput
/// calls when Settings › Phone remote allows them. While one of those screens is open the phone asks for the
/// foreground app's name (<c>orbit.focus.watch</c>), shown as "Typing into …".
/// </summary>
public sealed partial class OrbitCore
{
    /// <summary>Longest text one <c>orbit.input</c> may type.</summary>
    public const int MaxTypedText = 1000;

    DateTime _lastInputAt = DateTime.MinValue, _lastSlidesAt = DateTime.MinValue;
    readonly object _focusGate = new();
    System.Threading.Timer? _focusTimer;
    string _lastFocusApp = "";

    void OnRemoteInput(JsonObject b)
    {
        if (!Settings.RemoteInput) return;
        NoteRemoteUse(ref _lastInputAt, "is using this PC's mouse and keyboard");
        switch (b["op"]?.GetValue<string>())
        {
            case "move":
                InputInjector.Move(Clamp(b["dx"], 2000), Clamp(b["dy"], 2000));
                break;
            case "button":
                InputInjector.Button(b["button"]?.GetValue<string>() ?? "left", b["action"]?.GetValue<string>() ?? "click");
                break;
            case "scroll":
                InputInjector.Scroll(Clamp(b["dy"], 12000), Clamp(b["dx"], 12000));
                break;
            case "text":
                var text = b["text"]?.GetValue<string>() ?? "";
                if (text.Length is > 0 and <= MaxTypedText) InputInjector.Text(text);
                break;
            case "key":
                var mods = b["mods"] is JsonArray a ? a.Select(m => m?.GetValue<string>() ?? "").ToList() : new List<string>();
                InputInjector.Press(b["key"]?.GetValue<string>(), mods);
                break;
        }
    }

    static int Clamp(JsonNode? n, int max)
    {
        try { return n == null ? 0 : Math.Clamp(n.GetValue<int>(), -max, max); }
        catch { return 0; }
    }

    /// <summary>
    /// Presentation keys: arrows, Esc and B work in PowerPoint, Google Slides and most PDF viewers. Start is F5 in
    /// apps, but Ctrl+F5 in a browser, where F5 alone would reload the page (Google Slides presents on Ctrl+F5).
    /// </summary>
    public static (string? key, string[] mods)? SlidesKey(string op, bool browser = false) => op switch
    {
        "next" => ("right", Array.Empty<string>()),
        "prev" => ("left", Array.Empty<string>()),
        "start" => ("f5", browser ? new[] { "ctrl" } : Array.Empty<string>()),
        "end" => ("esc", Array.Empty<string>()),
        "black" => ("b", Array.Empty<string>()),
        _ => null
    };

    void OnSlides(JsonObject b)
    {
        var op = b["op"]?.GetValue<string>() ?? "";
        if (!Settings.RemoteSlides || SlidesKey(op, op == "start" && InputInjector.ForegroundIsBrowser()) is not { } k) return;
        NoteRemoteUse(ref _lastSlidesAt, "is presenting with the slides remote");
        InputInjector.Press(k.key, k.mods);
    }

    /// <summary>One notification when the phone starts using input after a while, so control is never silent.</summary>
    void NoteRemoteUse(ref DateTime last, string what)
    {
        var now = DateTime.UtcNow;
        bool fresh = now - last > TimeSpan.FromMinutes(5);
        last = now;
        if (!fresh) return;
        var phone = Phone.Name;
        Log.Write("remote input started");
        Ui.Run(() => Toasts.RemoteAction($"{phone} {what}", phone));
    }

    // ---------------------------------------------------------------- focused app

    void OnFocusWatch(JsonObject b)
    {
        bool on = b["on"]?.GetValue<bool>() == true && (Settings.RemoteInput || Settings.RemoteSlides);
        lock (_focusGate)
        {
            _focusTimer?.Dispose();
            _focusTimer = null;
            _lastFocusApp = "";
            if (on) _focusTimer = new System.Threading.Timer(_ => SendFocus(), null, 0, 700);
        }
    }

    void SendFocus()
    {
        // Skip a tick rather than overlap one that is still looking the name up.
        if (!Monitor.TryEnter(_focusGate)) return;
        try
        {
            if (_link is not { } link || _focusTimer == null) return;
            var app = InputInjector.ForegroundApp();
            if (app == _lastFocusApp) return;
            _lastFocusApp = app;
            link.Post("orbit.focus", new JsonObject { ["app"] = app });
        }
        finally { Monitor.Exit(_focusGate); }
    }

    /// <summary>On disconnect, and when input and slides are switched off.</summary>
    void StopFocusWatch()
    {
        lock (_focusGate)
        {
            _focusTimer?.Dispose();
            _focusTimer = null;
        }
    }
}
