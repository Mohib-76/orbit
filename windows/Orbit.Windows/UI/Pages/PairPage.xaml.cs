using System.Windows;
using System.Windows.Controls;
using Orbit.Core;

namespace Orbit.UI.Pages;

public partial class PairPage : UserControl
{
    public PairPage()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) => { if (IsVisible && App.Core.PairQr == null) App.Core.BeginPairing(); };
        App.Core.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(OrbitCore.PhoneApp) or nameof(OrbitCore.CanInstallPhoneApp)
                or nameof(OrbitCore.PhoneAppInstalling)) ShowInstall();
        };
        ShowInstall();
    }

    /// <summary>A plugged-in phone without Orbit gets "Install on phone" instead of "Pair over USB".</summary>
    void ShowInstall()
    {
        var core = App.Core;
        var offer = OrbitCore.BundledApk != null && (core.PhoneApp == PhoneAppState.Missing || core.PhoneAppInstalling);
        InstallBtn.Visibility = offer ? Visibility.Visible : Visibility.Collapsed;
        InstallHint.Visibility = offer && !core.PhoneAppInstalling ? Visibility.Visible : Visibility.Collapsed;
        UsbBtn.Visibility = offer ? Visibility.Collapsed : Visibility.Visible;
        InstallBtn.IsEnabled = core.CanInstallPhoneApp;
    }

    async void PairUsb_Click(object sender, RoutedEventArgs e)
    {
        UsbBtn.IsEnabled = false;
        UsbResult.Text = "Opening Orbit on your phone…";
        UsbResult.Text = await App.Core.PairOverUsbAsync();
        UsbBtn.IsEnabled = App.Core.CanPairOverUsb;
    }

    async void Install_Click(object sender, RoutedEventArgs e)
    {
        UsbResult.Text = "Installing Orbit on your phone… If the phone asks to allow it, tap Allow.";
        UsbResult.Text = await App.Core.InstallPhoneAppAsync();
    }
}
