using System.Windows;
using System.Windows.Threading;
using DoorsHandler.Desktop.Theming;
using DoorsHandler.Desktop.ViewModels;

namespace DoorsHandler.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        // Apply the saved theme before the window is created so it never flashes in the wrong colours.
        var settings = AppSettings.Load();
        ThemeManager.Apply(settings.Theme);

        var demo = e.Args.Any(a => string.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase));
        var window = new MainWindow(new MainViewModel(settings, demo));
        MainWindow = window;
        window.Show();
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"Unexpected error:\n\n{e.Exception.Message}", AppInfo.Name,
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
