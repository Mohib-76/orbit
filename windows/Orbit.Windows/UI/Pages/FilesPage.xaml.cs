using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Orbit.Core;

namespace Orbit.UI.Pages;

public partial class FilesPage : UserControl
{
    OrbitCore Core => App.Core;

    public FilesPage()
    {
        InitializeComponent();
        Core.Transfers.CollectionChanged += (_, _) => Refresh();
        Core.Phone.PropertyChanged += (_, _) => Refresh();
        DropZone.Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] f) { Core.SendFiles(f); e.Handled = true; }
        };
        Refresh();
    }

    void Refresh()
    {
        Empty.Visibility = Core.Transfers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DropLabel.Text = $"Drop files to send to {Core.Phone.Name}";
    }

    static Transfer? T(object s) => (s as FrameworkElement)?.Tag as Transfer;

    void Browse_Click(object s, MouseButtonEventArgs e) => App.Current.PickAndSendFiles();
    void Accept_Click(object s, RoutedEventArgs e) { if (T(s) is { } t) Core.Accept(t); }
    void Retry_Click(object s, RoutedEventArgs e) { if (T(s) is { } t) Core.Retry(t); }
    void Cancel_Click(object s, RoutedEventArgs e) { if (T(s) is { } t) { if (t.NeedsAnswer) Core.Decline(t); else Core.Cancel(t); } }
    void ClearFinished_Click(object s, RoutedEventArgs e) => Core.ClearFinished();

    void Open_Click(object s, RoutedEventArgs e)
    {
        if (T(s) is { } t && File.Exists(t.LocalPath)) Process.Start(new ProcessStartInfo(t.LocalPath) { UseShellExecute = true });
    }

    void Show_Click(object s, RoutedEventArgs e)
    {
        if (T(s) is { } t) Process.Start("explorer.exe", $"/select,\"{t.LocalPath}\"");
    }

    void OpenFolder_Click(object s, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Core.Settings.SaveFolder);
        Process.Start("explorer.exe", $"\"{Core.Settings.SaveFolder}\"");
    }
}
