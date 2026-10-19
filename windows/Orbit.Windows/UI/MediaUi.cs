using System.Windows.Media;
using System.Windows.Media.Imaging;
using Orbit.Core;
using Orbit.Core.Media;

namespace Orbit.UI;

/// <summary>Small helpers shared by the phone player views (Home card, tray row).</summary>
public static class MediaUi
{
    static readonly Color[] Palette =
    {
        Color.FromRgb(0x1A, 0x9E, 0x5C), Color.FromRgb(0xC2, 0x5A, 0x00), Color.FromRgb(0x3E, 0x5C, 0x76),
        Color.FromRgb(0x6A, 0x3F, 0xD9), Color.FromRgb(0x0E, 0x74, 0x90), Color.FromRgb(0xC9, 0x37, 0x2C)
    };

    public static Brush Tint(string app)
    {
        int h = 0;
        foreach (var ch in app) h = h * 31 + ch;
        var b = new SolidColorBrush(Palette[Math.Abs(h % Palette.Length)]);
        b.Freeze();
        return b;
    }

    public static string Initial(string app) => app.Length > 0 ? char.ToUpper(app[0]).ToString() : "♪";

    /// <summary>The phone app's icon (if one of its notifications brought it), else null.</summary>
    public static BitmapImage? AppIcon(PlayerInfo p)
    {
        var path = OrbitCore.PhoneAppIcon(p.Id);
        if (path == null) return null;
        try
        {
            var img = new BitmapImage();
            img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.UriSource = new Uri(path); img.EndInit(); img.Freeze();
            return img;
        }
        catch { return null; }
    }

    /// <summary>"Artist · Album", whichever exist.</summary>
    public static string Subtitle(PlayerInfo p) =>
        string.Join(" · ", new[] { p.Artist, p.Album }.Where(s => !string.IsNullOrWhiteSpace(s)));
}
