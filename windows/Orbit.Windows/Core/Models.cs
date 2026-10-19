using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace Orbit.Core;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class Ui
{
    public static void Run(Action a)
    {
        var d = Application.Current?.Dispatcher;
        if (d == null) return;
        if (d.CheckAccess()) a(); else d.BeginInvoke(a);
    }
}

public enum LinkState { NotPaired, Searching, Connected, Pairing }

public sealed class PhoneState : Observable
{
    LinkState _state = LinkState.NotPaired;
    string _name = "", _model = "", _link = "", _chargeInfo = "";
    int _battery = -1;
    bool _charging;
    DateTime _connectedSince;

    public LinkState State { get => _state; set { if (Set(ref _state, value)) { Raise(nameof(IsConnected)); Raise(nameof(StatusText)); Raise(nameof(IsPaired)); } } }
    public bool IsConnected => _state == LinkState.Connected;
    public bool IsPaired => _state is LinkState.Connected or LinkState.Searching;
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Model { get => _model; set => Set(ref _model, value); }
    /// <summary>How we reach the phone: "USB tethering", "Phone hotspot", "USB cable", "Wi-Fi".</summary>
    public string Link { get => _link; set { if (Set(ref _link, value)) Raise(nameof(StatusText)); } }
    public int Battery { get => _battery; set { if (Set(ref _battery, value)) { Raise(nameof(BatteryText)); Raise(nameof(BatteryFraction)); } } }
    public bool Charging { get => _charging; set { if (Set(ref _charging, value)) Raise(nameof(ChargeText)); } }
    public string ChargeInfo { get => _chargeInfo; set { if (Set(ref _chargeInfo, value)) Raise(nameof(ChargeText)); } }
    public DateTime ConnectedSince { get => _connectedSince; set { Set(ref _connectedSince, value); Raise(nameof(StatusText)); } }

    public string BatteryText => _battery < 0 ? "–" : $"{_battery}%";
    public double BatteryFraction => _battery < 0 ? 0 : _battery / 100.0;
    public string ChargeText => _charging ? (string.IsNullOrEmpty(_chargeInfo) ? "Charging" : $"Charging · {_chargeInfo}") : "On battery";

    public string StatusText => _state switch
    {
        LinkState.Connected => $"Connected for {Ago(DateTime.Now - _connectedSince)}" + (string.IsNullOrEmpty(_link) ? "" : $" · {_link}"),
        LinkState.Searching => "Looking for your phone…",
        LinkState.Pairing => "Waiting for your phone…",
        _ => "No phone paired"
    };

    public void Tick() => Raise(nameof(StatusText));

    static string Ago(TimeSpan t) =>
        t.TotalMinutes < 1 ? "a moment" :
        t.TotalHours < 1 ? $"{(int)t.TotalMinutes} min" :
        $"{(int)t.TotalHours} h {t.Minutes} min";
}

public sealed class ClipItem
{
    public string Text { get; init; } = "";
    public bool FromPhone { get; init; }
    public DateTime Time { get; init; } = DateTime.Now;
    public string Direction => FromPhone ? "Phone → PC" : "PC → Phone";
    public string Preview => Text.Length > 300 ? Text[..300] + "…" : Text;
}

public sealed class PhoneNotification : Observable
{
    public string Key { get; init; } = "";
    public string App { get; init; } = "";
    public string Package { get; init; } = "";
    public string Title { get; init; } = "";
    public string Text { get; init; } = "";
    /// <summary>Title and text on one line each, for the compact lists (Home, tray).</summary>
    public string TitleLine => OneLine(Title);
    public string TextLine => OneLine(Text);
    static string OneLine(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"\s*[\r\n]+\s*", " · ").Trim(' ', '·');
    public DateTime Time { get; init; } = DateTime.Now;
    public string? IconPath { get; init; }
    public ImageSource? Icon { get; init; }
    public string Initial => string.IsNullOrEmpty(App) ? "?" : App[..1].ToUpperInvariant();
    public Brush Tint => new SolidColorBrush(Palette[Math.Abs(Package.GetHashCode()) % Palette.Length]);
    public string TimeText
    {
        get
        {
            var t = DateTime.Now - Time;
            return t.TotalMinutes < 1 ? "now" : t.TotalHours < 1 ? $"{(int)t.TotalMinutes}m" : t.TotalDays < 1 ? $"{(int)t.TotalHours}h" : Time.ToString("d MMM");
        }
    }
    public void Tick() => Raise(nameof(TimeText));

    static readonly Color[] Palette =
    {
        Color.FromRgb(0x1A,0x9E,0x5C), Color.FromRgb(0xC9,0x37,0x2C), Color.FromRgb(0x6A,0x3F,0xD9),
        Color.FromRgb(0x1F,0x6F,0xD1), Color.FromRgb(0xC2,0x6A,0x00), Color.FromRgb(0x0E,0x7C,0x86)
    };
}

public enum TransferState { Waiting, Queued, Running, Paused, Done, Failed, Cancelled }

public sealed class Transfer : Observable
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "";
    public long Size { get; init; }
    public bool Outgoing { get; init; }
    /// <summary>Source file (outgoing) or final destination (incoming).</summary>
    public string LocalPath { get; set; } = "";

    long _done;
    TransferState _state = TransferState.Queued;
    double _speed;
    string? _error;

    public long Done { get => _done; set { if (Set(ref _done, value)) { Raise(nameof(Progress)); Raise(nameof(Detail)); } } }
    public TransferState State
    {
        get => _state;
        set { if (Set(ref _state, value)) { Raise(nameof(Detail)); Raise(nameof(IsActive)); Raise(nameof(IsDone)); Raise(nameof(CanRetry)); Raise(nameof(NeedsAnswer)); } }
    }
    public double Speed { get => _speed; set { if (Set(ref _speed, value)) Raise(nameof(Detail)); } }
    public string? Error { get => _error; set { Set(ref _error, value); Raise(nameof(Detail)); } }

    public double Progress => Size <= 0 ? (State == TransferState.Done ? 1 : 0) : Math.Min(1, (double)_done / Size);
    public bool IsActive => _state is TransferState.Queued or TransferState.Running or TransferState.Paused or TransferState.Waiting;
    public bool IsDone => _state == TransferState.Done;
    public bool CanRetry => _state is TransferState.Failed or TransferState.Paused;
    public bool NeedsAnswer => _state == TransferState.Waiting && !Outgoing;
    public string Arrow => Outgoing ? "to phone" : "from phone";
    public string Ext
    {
        get
        {
            var e = System.IO.Path.GetExtension(Name).TrimStart('.').ToUpperInvariant();
            return e.Length is > 0 and <= 4 ? e : "FILE";
        }
    }

    public string Detail => _state switch
    {
        TransferState.Running => $"{(int)(Progress * 100)}% · {FormatSize(_speed)}/s" + Eta(),
        TransferState.Queued => $"Queued · {FormatSize(Size)}",
        TransferState.Waiting => $"Waiting for you to accept · {FormatSize(Size)}",
        TransferState.Paused => $"Paused at {(int)(Progress * 100)}% · resumes when connected",
        TransferState.Done => $"{FormatSize(Size)} · {Arrow}",
        TransferState.Cancelled => "Cancelled",
        TransferState.Failed => "Failed" + (string.IsNullOrEmpty(_error) ? "" : $" · {_error}"),
        _ => ""
    };

    string Eta()
    {
        if (_speed <= 1) return "";
        var s = (Size - _done) / _speed;
        return s < 60 ? $" · {Math.Max(1, (int)s)} s left" : $" · {(int)(s / 60)} min left";
    }

    public static string FormatSize(double b) =>
        b < 1024 ? $"{b:0} B" : b < 1048576 ? $"{b / 1024:0.#} KB" : b < 1073741824 ? $"{b / 1048576:0.#} MB" : $"{b / 1073741824:0.##} GB";
}
