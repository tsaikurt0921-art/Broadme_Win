using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using QRCoder;

namespace Broadme.Win.Views;

public partial class QrCodeWindow : Window
{
    private readonly string _url;

    public QrCodeWindow(string url)
    {
        _url = url;
        InitializeComponent();
        UrlText.Text = _url;
        QrImage.Source = BuildQr(_url);
    }

    private static BitmapImage BuildQr(string value)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(value, QRCodeGenerator.ECCLevel.H);
        var png = new PngByteQRCode(data).GetGraphic(20);

        using var ms = new MemoryStream(png);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = ms;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void CopyClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_url);
    }

    private void CloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
