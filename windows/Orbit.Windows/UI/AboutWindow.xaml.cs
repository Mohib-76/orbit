using System.Diagnostics;
using System.IO;
using System.Windows;
using Orbit.Core;

namespace Orbit.UI;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {Proto.AppVersion} · protocol {Proto.Version}" +
            (App.Core.PeerAppVersion.Length > 0 ? $" · phone app {App.Core.PeerAppVersion}" : "");
        var notices = Path.Combine(AppContext.BaseDirectory, "THIRD_PARTY_NOTICES.md");
        Notices.Text = File.Exists(notices) ? File.ReadAllText(notices) : "See THIRD_PARTY_NOTICES.md in the source repository.";
    }

    static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    void Repo_Click(object s, RoutedEventArgs e) => Open(AppInfo.RepoUrl);
    void Issue_Click(object s, RoutedEventArgs e) => Open(AppInfo.IssuesUrl);
    void Releases_Click(object s, RoutedEventArgs e) => Open(AppInfo.ReleasesUrl);
    void Privacy_Click(object s, RoutedEventArgs e) => Open(AppInfo.PrivacyUrl);
    void Close_Click(object s, RoutedEventArgs e) => Close();

    void License_Click(object s, RoutedEventArgs e)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "LICENSE");
        if (File.Exists(path)) Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
        else Open("https://www.gnu.org/licenses/agpl-3.0.html");
    }
}
