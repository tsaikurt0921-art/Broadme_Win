using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace Broadme.Win;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void CloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OpenLineClick(object sender, MouseButtonEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://line.me/R/ti/p/@broadme") { UseShellExecute = true });
    }
}
