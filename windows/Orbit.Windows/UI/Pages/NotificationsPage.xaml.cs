using System.Windows;
using System.Windows.Controls;
using Orbit.Core;

namespace Orbit.UI.Pages;

public partial class NotificationsPage : UserControl
{
    OrbitCore Core => App.Core;

    public NotificationsPage()
    {
        InitializeComponent();
        Core.Notifications.CollectionChanged += (_, _) => Refresh();
        Core.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(OrbitCore.DoNotDisturb)) Refresh(); };
        Refresh();
    }

    void Refresh()
    {
        Empty.Visibility = Core.Notifications.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PauseText.Text = Core.DoNotDisturb ? "Resume" : "Pause 1 h";
    }

    void Dismiss_Click(object s, RoutedEventArgs e)
    {
        if (s is Button { Tag: string key }) Core.DismissNotification(key);
    }

    void Clear_Click(object s, RoutedEventArgs e) => Core.ClearNotifications();
    void Pause_Click(object s, RoutedEventArgs e) => Core.PauseNotifications(Core.DoNotDisturb ? null : TimeSpan.FromHours(1));
}
