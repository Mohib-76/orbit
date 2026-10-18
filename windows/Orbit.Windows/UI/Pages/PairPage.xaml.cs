using System.Windows;
using System.Windows.Controls;

namespace Orbit.UI.Pages;

public partial class PairPage : UserControl
{
    public PairPage()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) => { if (IsVisible && App.Core.PairQr == null) App.Core.BeginPairing(); };
    }

    async void PairUsb_Click(object sender, RoutedEventArgs e)
    {
        UsbBtn.IsEnabled = false;
        UsbResult.Text = "Opening Orbit on your phone…";
        UsbResult.Text = await App.Core.PairOverUsbAsync();
        UsbBtn.IsEnabled = App.Core.CanPairOverUsb;
    }
}
