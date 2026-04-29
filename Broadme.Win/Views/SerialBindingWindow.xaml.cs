using System.Windows;
using Broadme.Win.Services.Auth;

namespace Broadme.Win.Views;

public partial class SerialBindingWindow : Window
{
    private readonly SerialManager _serial;
    private bool _isBinding;

    public SerialBindingWindow(SerialManager serial)
    {
        _serial = serial;
        InitializeComponent();
    }

    private async void BindClick(object sender, RoutedEventArgs e)
    {
        if (_isBinding) return;

        var value = SerialTextBox.Text?.Trim().ToUpperInvariant() ?? string.Empty;
        MessageText.Text = string.Empty;
        MessageText.Foreground = System.Windows.Media.Brushes.Red;

        if (!_serial.IsValidFormat(value))
        {
            MessageText.Text = "序號格式錯誤，請檢查後重試";
            return;
        }

        try
        {
            _isBinding = true;
            SerialTextBox.IsEnabled = false;
            BindButton.IsEnabled = false;
            BindButton.Content = "...";

            var (ok, message) = await _serial.ValidateAndBindAsync(value);
            if (!ok)
            {
                MessageText.Text = message;
                return;
            }

            MessageText.Foreground = System.Windows.Media.Brushes.Green;
            MessageText.Text = string.IsNullOrWhiteSpace(message) ? "綁定成功" : message;
            DialogResult = true;
            Close();
        }
        finally
        {
            _isBinding = false;
            SerialTextBox.IsEnabled = true;
            BindButton.IsEnabled = true;
            BindButton.Content = "綁定";
        }
    }

    private void CloseClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
