using System.Windows;
using System.Windows.Controls;
using Orbit.Core;

namespace Orbit.UI.Pages;

public partial class ClipboardPage : UserControl
{
    public ClipboardPage()
    {
        InitializeComponent();
        App.Core.ClipHistory.CollectionChanged += (_, _) => Refresh();
        Refresh();
    }

    void Refresh() => Empty.Visibility = App.Core.ClipHistory.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    void Send_Click(object s, RoutedEventArgs e) => App.Current.SendClipboardNow();
    void Clear_Click(object s, RoutedEventArgs e) => App.Core.ClipHistory.Clear();
    void Save(object s, RoutedEventArgs e) { App.Core.Settings.Save(); App.Core.SendSettings(); }

    void Copy_Click(object s, RoutedEventArgs e)
    {
        if (s is Button { Tag: string text }) ClipboardHost.SetText(text);
    }
}
