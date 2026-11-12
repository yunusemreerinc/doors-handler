using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DoorsHandler.Desktop.ViewModels;

namespace DoorsHandler.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    /// <summary>Double-click opens a folder/project; on a module it copies the module's full path.</summary>
    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Ignore double-clicks on the header or empty area.
        if (FindParent<DataGridRow>(e.OriginalSource as DependencyObject) is null) return;

        var command = _viewModel.SelectedItem is { IsModule: false }
            ? _viewModel.BrowseIntoCommand
            : _viewModel.CopyPathCommand;
        if (command.CanExecute(null)) command.Execute(null);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosing(e);
    }

    private static T? FindParent<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null and not T)
        {
            element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }

        return element as T;
    }
}
