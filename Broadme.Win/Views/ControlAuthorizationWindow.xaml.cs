using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;

namespace Broadme.Win.Views;

public partial class ControlAuthorizationWindow : Window
{
    private readonly Action<string> _onAuthorize;
    private static readonly Regex NumberRegex = new("^[0-9]$");
    private readonly string _controlUrl;
    private readonly string _selectedIp;

    public ControlAuthorizationWindow(Action<string> onAuthorize, int port = 8080, string? selectedIp = null)
    {
        _onAuthorize = onAuthorize;
        _selectedIp = string.IsNullOrWhiteSpace(selectedIp) ? "localhost" : selectedIp;
        _controlUrl = BuildControlUrl(port);

        InitializeComponent();

        ControlUrlText.Text = _controlUrl;
        QrImage.Source = BuildQr(_controlUrl);

        Loaded += (_, _) => Pin1.Focus();
    }

    private void PinTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !NumberRegex.IsMatch(e.Text);
    }

    private void PinChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox box) return;
        if (box.Text.Length == 1)
        {
            switch (box.Name)
            {
                case "Pin1": Pin2.Focus(); break;
                case "Pin2": Pin3.Focus(); break;
                case "Pin3": Pin4.Focus(); break;
                case "Pin4": Pin5.Focus(); break;
                case "Pin5": Pin6.Focus(); break;
            }
        }

        AuthButton.IsEnabled = GetPin().Length == 6;
    }

    private void ClearClick(object sender, RoutedEventArgs e)
    {
        Pin1.Text = "";
        Pin2.Text = "";
        Pin3.Text = "";
        Pin4.Text = "";
        Pin5.Text = "";
        Pin6.Text = "";
        StatusText.Text = "";
        Pin1.Focus();
    }

    private void AuthClick(object sender, RoutedEventArgs e)
    {
        var pin = GetPin();
        if (pin.Length != 6)
        {
            StatusText.Text = "請輸入完整 6 位 PIN";
            StatusText.Foreground = System.Windows.Media.Brushes.Red;
            return;
        }

        _onAuthorize(pin);
        StatusText.Text = $"PIN {pin} 已授權";
        StatusText.Foreground = System.Windows.Media.Brushes.Green;
        DialogResult = true;
        Close();
    }

    private string GetPin() => $"{Pin1.Text}{Pin2.Text}{Pin3.Text}{Pin4.Text}{Pin5.Text}{Pin6.Text}";

    private static BitmapImage BuildQr(string value)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(value, QRCodeGenerator.ECCLevel.H);
        var png = new PngByteQRCode(data).GetGraphic(10);

        using var ms = new MemoryStream(png);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = ms;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private string BuildControlUrl(int port)
    {
        return $"http://{_selectedIp}:{port}/control";
    }
}
