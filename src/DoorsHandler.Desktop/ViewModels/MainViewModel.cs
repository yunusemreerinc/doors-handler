using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using DoorsHandler.Core;
using DoorsHandler.Core.Dxl;
using DoorsHandler.Desktop.Doors;
using DoorsHandler.Desktop.Mvvm;
using Microsoft.Win32;

namespace DoorsHandler.Desktop.ViewModels;

public enum ConnectionMode
{
    DoorsCom,
    Demo,
}

public sealed record Option<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

public enum ItemFilter
{
    AllModules,
    Formal,
    Link,
    Descriptive,
    FoldersAndProjects,
    Everything,
}

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private IDoorsClient? _client;
    private CancellationTokenSource? _listCts;

    private Option<ConnectionMode> _selectedMode;
    private Option<ItemFilter> _selectedFilter;
    private DoorsSessionInfo? _session;
    private bool _isBusy;
    private string _rootPath = "/";
    private bool _recursive = true;
    private string _filterText = string.Empty;
    private string _statusText = "Bağlı değil. Başlamak için \"Bağlan\" düğmesine basın.";
    private string _lastListedRoot = string.Empty;
    private DoorsItem? _selectedItem;

    public MainViewModel(bool demo = false)
    {
        ConnectionModes =
        [
            new(ConnectionMode.DoorsCom, "IBM DOORS (yerel istemci)"),
            new(ConnectionMode.Demo, "Demo verisi (DOORS gerekmez)"),
        ];
        _selectedMode = ConnectionModes[demo ? 1 : 0];

        Filters =
        [
            new(ItemFilter.AllModules, "Tüm modüller"),
            new(ItemFilter.Formal, "Formal modüller"),
            new(ItemFilter.Link, "Link modülleri"),
            new(ItemFilter.Descriptive, "Descriptive modüller"),
            new(ItemFilter.FoldersAndProjects, "Klasörler ve projeler"),
            new(ItemFilter.Everything, "Tüm öğeler"),
        ];
        _selectedFilter = Filters[0];

        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = FilterItem;
        ItemsView.SortDescriptions.Add(new SortDescription(nameof(DoorsItem.FullName), ListSortDirection.Ascending));

        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsConnected && !IsBusy);
        DisconnectCommand = new RelayCommand(_ => Disconnect(), _ => IsConnected && !IsBusy);
        ListCommand = new AsyncRelayCommand(ListAsync, () => IsConnected && !IsBusy);
        CancelCommand = new RelayCommand(_ => _listCts?.Cancel(), _ => IsBusy && _listCts is not null);
        ExportCommand = new RelayCommand(_ => Export(), _ => !IsBusy && VisibleCount > 0);
        CopyPathCommand = new RelayCommand(_ => CopyPath(), _ => SelectedItem is not null);
        BrowseIntoCommand = new AsyncRelayCommand(BrowseIntoAsync,
            () => IsConnected && !IsBusy && SelectedItem is { IsModule: false });
        GoUpCommand = new AsyncRelayCommand(GoUpAsync, () => IsConnected && !IsBusy && RootPath.Trim() is not ("" or "/"));
    }

    public ObservableCollection<DoorsItem> Items { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];
    public ICollectionView ItemsView { get; }

    public IReadOnlyList<Option<ConnectionMode>> ConnectionModes { get; }
    public IReadOnlyList<Option<ItemFilter>> Filters { get; }

    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand ListCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand CopyPathCommand { get; }
    public ICommand BrowseIntoCommand { get; }
    public ICommand GoUpCommand { get; }

    public Option<ConnectionMode> SelectedMode
    {
        get => _selectedMode;
        set => SetProperty(ref _selectedMode, value);
    }

    public Option<ItemFilter> SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetProperty(ref _selectedFilter, value)) RefreshView();
        }
    }

    public bool IsConnected => _session is not null;
    public bool CanChangeMode => !IsConnected && !IsBusy;

    public string SessionText => _session is null
        ? "Bağlı değil"
        : $"{_session.UserName} @ {_session.DatabaseName} · DOORS {_session.Version}";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value)) OnPropertyChanged(nameof(CanChangeMode));
        }
    }

    public string RootPath
    {
        get => _rootPath;
        set => SetProperty(ref _rootPath, value);
    }

    public bool Recursive
    {
        get => _recursive;
        set => SetProperty(ref _recursive, value);
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value)) RefreshView();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public DoorsItem? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    // Cached in RefreshView(): command CanExecute checks read it very often.
    public int VisibleCount { get; private set; }
    public int ModuleCount { get; private set; }
    public bool HasWarnings => Warnings.Count > 0;

    public string CountText => Items.Count == 0
        ? string.Empty
        : $"{VisibleCount} / {Items.Count} öğe gösteriliyor · toplam {ModuleCount} modül";

    private async Task ConnectAsync()
    {
        IsBusy = true;
        StatusText = SelectedMode.Value == ConnectionMode.Demo
            ? "Demo verisine bağlanılıyor…"
            : "DOORS'a bağlanılıyor… (DOORS kapalıysa açılacak ve oturum açmanız istenecek)";
        try
        {
            _client = CreateClient(SelectedMode.Value);
            SetSession(await _client.ConnectAsync());
            StatusText = $"Bağlandı: {SessionText}";
        }
        catch (Exception ex)
        {
            _client?.Dispose();
            _client = null;
            SetSession(null);
            StatusText = "Bağlantı başarısız.";
            ShowError("Bağlantı hatası", ex);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await ListAsync();
    }

    private static IDoorsClient CreateClient(ConnectionMode mode) => mode switch
    {
        ConnectionMode.Demo => new DemoDoorsClient(),
        _ => new DxlDoorsClient(new ComDxlExecutor()),
    };

    private void Disconnect()
    {
        _client?.Dispose();
        _client = null;
        SetSession(null);
        Items.Clear();
        Warnings.Clear();
        _lastListedRoot = string.Empty;
        RefreshView();
        StatusText = "Bağlantı kesildi.";
    }

    private async Task ListAsync()
    {
        if (_client is null) return;

        IsBusy = true;
        _listCts = new CancellationTokenSource();
        var root = DxlScripts.NormalizePath(RootPath);
        RootPath = root;
        StatusText = Recursive
            ? $"'{root}' altındaki tüm modüller taranıyor… Büyük veritabanlarında bu biraz sürebilir."
            : $"'{root}' listeleniyor…";
        try
        {
            var listing = await _client.ListItemsAsync(root, Recursive, _listCts.Token);
            Items.Clear();
            foreach (var item in listing.Items) Items.Add(item);
            Warnings.Clear();
            foreach (var warning in listing.Warnings) Warnings.Add(warning);
            _lastListedRoot = root;
            RefreshView();
            StatusText = $"'{root}' listelendi ({DateTime.Now:HH:mm:ss}).";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Listeleme iptal edildi.";
        }
        catch (Exception ex)
        {
            StatusText = "Listeleme başarısız.";
            if (ex is DoorsConnectionLostException)
            {
                Disconnect();
            }

            ShowError("Listeleme hatası", ex);
        }
        finally
        {
            _listCts.Dispose();
            _listCts = null;
            IsBusy = false;
        }
    }

    private async Task BrowseIntoAsync()
    {
        if (SelectedItem is not { IsModule: false } folder) return;
        RootPath = folder.FullName;
        await ListAsync();
    }

    private async Task GoUpAsync()
    {
        var root = DxlScripts.NormalizePath(RootPath);
        var index = root.LastIndexOf('/');
        RootPath = index <= 0 ? "/" : root[..index];
        await ListAsync();
    }

    private void Export()
    {
        var safeName = string.Join("_", (_lastListedRoot.Trim('/') is { Length: > 0 } r ? r : "doors")
            .Split(Path.GetInvalidFileNameChars().Append('/').ToArray(), StringSplitOptions.RemoveEmptyEntries));
        var dialog = new SaveFileDialog
        {
            Title = "Modül listesini dışa aktar",
            Filter = "CSV dosyası (*.csv)|*.csv",
            FileName = $"{safeName}_moduller_{DateTime.Now:yyyyMMdd_HHmm}.csv",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            CsvExporter.Write(dialog.FileName, ItemsView.Cast<DoorsItem>());
            StatusText = $"{VisibleCount} öğe dışa aktarıldı: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            ShowError("Dışa aktarma hatası", ex);
        }
    }

    private void CopyPath()
    {
        if (SelectedItem is null) return;
        try
        {
            Clipboard.SetText(SelectedItem.FullName);
            StatusText = $"Panoya kopyalandı: {SelectedItem.FullName}";
        }
        catch (Exception ex)
        {
            ShowError("Pano hatası", ex);
        }
    }

    private bool FilterItem(object obj)
    {
        if (obj is not DoorsItem item) return false;

        var typeMatches = SelectedFilter.Value switch
        {
            ItemFilter.AllModules => item.IsModule,
            ItemFilter.Formal => item.Type == DoorsItemType.Formal,
            ItemFilter.Link => item.Type == DoorsItemType.Link,
            ItemFilter.Descriptive => item.Type == DoorsItemType.Descriptive,
            ItemFilter.FoldersAndProjects => !item.IsModule,
            _ => true,
        };
        if (!typeMatches) return false;

        var text = FilterText.Trim();
        if (text.Length == 0) return true;

        var compare = CultureInfo.CurrentCulture.CompareInfo;
        return new[] { item.Name, item.FullName, item.Description, item.UniqueId }
            .Any(v => compare.IndexOf(v, text, CompareOptions.IgnoreCase) >= 0);
    }

    private void RefreshView()
    {
        ItemsView.Refresh();
        VisibleCount = ItemsView.Cast<object>().Count();
        ModuleCount = Items.Count(i => i.IsModule);
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(ModuleCount));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(HasWarnings));
    }

    private void SetSession(DoorsSessionInfo? session)
    {
        _session = session;
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(CanChangeMode));
        OnPropertyChanged(nameof(SessionText));
    }

    private static void ShowError(string title, Exception ex)
    {
        var message = ex is DoorsException or InvalidOperationException
            ? ex.Message
            : $"Beklenmeyen hata: {ex.Message}";
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public void Dispose()
    {
        _listCts?.Cancel();
        _client?.Dispose();
        _client = null;
    }
}
