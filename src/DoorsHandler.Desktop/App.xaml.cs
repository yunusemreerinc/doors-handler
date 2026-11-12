using System.Windows;
using System.Windows.Threading;
using DoorsHandler.Desktop.ViewModels;

namespace DoorsHandler.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        var demo = e.Args.Any(a => string.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase));
        var window = new MainWindow(new MainViewModel(demo));
        MainWindow = window;
        window.Show();
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"Beklenmeyen hata:\n\n{e.Exception.Message}", "DOORS Handler",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
