using Broadme.Win.Services.Auth;
using Broadme.Win.Views;

namespace Broadme.Win;

public partial class App : System.Windows.Application
{
    private readonly SerialManager _serial = new();

    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        var launch = new LaunchWindow();
        launch.Show();
        await Task.Delay(1500);

        var hasSaved = !string.IsNullOrWhiteSpace(_serial.SavedSerialNumber);
        if (hasSaved)
        {
            var (ok, _) = await _serial.RevalidateSavedSerialAsync();
            if (!ok)
            {
                _serial.ClearSerial();
                hasSaved = false;
            }
        }

        launch.Close();

        if (!hasSaved)
        {
            var binding = new SerialBindingWindow(_serial);
            var ok = binding.ShowDialog();
            if (ok != true)
            {
                Shutdown();
                return;
            }
        }

        var main = new MainWindow();
        main.Show();
    }
}
