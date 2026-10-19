using System.Windows;
using Orbit.Core;

namespace Orbit.UI;

/// <summary>"Turn off USB tethering?": asked before leaving tethering while this PC is online through the phone.</summary>
public partial class UsbConfirmWindow : Window
{
    public bool DontAskAgain => DontAsk.IsChecked == true;

    public UsbConfirmWindow(string phone, string targetMode)
    {
        InitializeComponent();
        var label = UsbModes.Label(targetMode);
        Body.Text = $"This laptop gets its internet from {phone}. It will be offline while {label} is on.";
        Confirm.Content = $"Switch to {label}";
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove(); };
    }

    void Confirm_Click(object s, RoutedEventArgs e) => DialogResult = true;
    void Cancel_Click(object s, RoutedEventArgs e) => DialogResult = false;
}
