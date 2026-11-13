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
using DoorsHandler.Desktop.Theming;
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
    private CancellationTokenSource? _operationCts;

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
    private DoorsErrorReport? _error;
    private IReadOnlyList<DoorsItem> _selectedItems = [];
    private Func<Task>? _retry;

    private readonly AppSettings _settings;
    private Option<AppTheme> _selectedTheme;

    public MainViewModel(AppSettings settings, bool demo = false)
    {
        _settings = settings;
        Themes =
        [
            new(AppTheme.System, "Sistem"),
            new(AppTheme.Light, "Açık"),
            new(AppTheme.Dark, "Koyu"),
        ];
        _selectedTheme = Themes.First(t => t.Value == ThemeManager.Selected);

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
        CancelCommand = new RelayCommand(_ => _operationCts?.Cancel(), _ => IsBusy && _operationCts is not null);
        ExportCommand = new RelayCommand(_ => Export(), _ => !IsBusy && VisibleCount > 0);
        CopyPathCommand = new RelayCommand(_ => CopyPath(), _ => SelectedItem is not null);
        BrowseIntoCommand = new AsyncRelayCommand(BrowseIntoAsync,
            () => IsConnected && !IsBusy && SelectedItem is { IsModule: false });
        GoUpCommand = new AsyncRelayCommand(GoUpAsync, () => IsConnected && !IsBusy && RootPath.Trim() is not ("" or "/"));
        ExportJsonCommand = new AsyncRelayCommand(ExportJsonAsync,
            () => IsConnected && !IsBusy && SelectedFormalModules.Count > 0);
        RetryCommand = new AsyncRelayCommand(RetryAsync, () => Error is not null && !IsBusy);
        CopyErrorCommand = new RelayCommand(_ => CopyError(), _ => Error is not null);
        DismissErrorCommand = new RelayCommand(_ => Error = null, _ => Error is not null);
    }

    public ObservableCollection<DoorsItem> Items { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];
    public ICollectionView ItemsView { get; }

    public IReadOnlyList<Option<ConnectionMode>> ConnectionModes { get; }
    public IReadOnlyList<Option<ItemFilter>> Filters { get; }
    public IReadOnlyList<Option<AppTheme>> Themes { get; }

    /// <summary>Applies the chosen theme immediately and remembers it for the next start.</summary>
    public Option<AppTheme> SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (!SetProperty(ref _selectedTheme, value)) return;
            ThemeManager.Apply(value.Value);
            _settings.Theme = value.Value;
            _settings.Save();
        }
    }

    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand ListCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand CopyPathCommand { get; }
    public ICommand BrowseIntoCommand { get; }
    public ICommand GoUpCommand { get; }
    public ICommand ExportJsonCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand CopyErrorCommand { get; }
    public ICommand DismissErrorCommand { get; }

    /// <summary>The last DOORS failure, shown in the error banner until dismissed or the next success.</summary>
    public DoorsErrorReport? Error
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value)) OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => Error is not null;

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

    /// <summary>Rows selected in the grid (multi-select). Set by the view.</summary>
    public IReadOnlyList<DoorsItem> SelectedItems
    {
        get => _selectedItems;
        set
        {
            _selectedItems = value;
            SelectedFormalModules = value.Where(i => i.Type == DoorsItemType.Formal).ToList();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedFormalModules));
            OnPropertyChanged(nameof(ExportJsonText));
        }
    }

    /// <summary>Only formal modules have objects to export.</summary>
    public IReadOnlyList<DoorsItem> SelectedFormalModules { get; private set; } = [];

    public string ExportJsonText => SelectedFormalModules.Count switch
    {
        0 => "Seçili modülleri JSON'a aktar…",
        1 => "Seçili modülü JSON'a aktar…",
        var n => $"Seçili {n} modülü JSON'a aktar…",
    };

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
        Error = null;
        _operationCts = new CancellationTokenSource();
        StatusText = SelectedMode.Value == ConnectionMode.Demo
            ? "Demo verisine bağlanılıyor…"
            : "DOORS'a bağlanılıyor… DOORS kapalıysa açılacak ve oturum açmanız istenecek.";
        try
        {
            _client = CreateClient(SelectedMode.Value);
            SetSession(await _client.ConnectAsync(_operationCts.Token));
            StatusText = $"Bağlandı: {SessionText}";
        }
        catch (Exception ex)
        {
            // The client may still be blocked inside DOORS (e.g. waiting for login);
            // disposing it abandons that call without freezing the window.
            _client?.Dispose();
            _client = null;
            SetSession(null);
            if (ex is OperationCanceledException)
            {
                StatusText = "Bağlantı iptal edildi.";
            }
            else
            {
                ReportError("DOORS bağlantısı kurulamadı", ex);
            }

            return;
        }
        finally
        {
            _operationCts.Dispose();
            _operationCts = null;
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
        Error = null;
        _operationCts = new CancellationTokenSource();
        var root = DxlScripts.NormalizePath(RootPath);
        RootPath = root;
        StatusText = Recursive
            ? $"'{root}' altındaki tüm modüller taranıyor… Büyük veritabanlarında bu biraz sürebilir."
            : $"'{root}' listeleniyor…";
        try
        {
            var listing = await _client.ListItemsAsync(root, Recursive, _operationCts.Token);
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
            var lost = ex is DoorsException { Kind: DoorsErrorKind.ConnectionLost };
            if (lost)
            {
                Disconnect();
            }

            ReportError(lost ? "DOORS bağlantısı koptu" : "Modüller listelenemedi", ex, root, ListAsync);
        }
        finally
        {
            _operationCts.Dispose();
            _operationCts = null;
            IsBusy = false;
        }
    }

    /// <summary>Reads the selected formal modules from DOORS and saves their objects as one JSON file.</summary>
    private async Task ExportJsonAsync()
    {
        var modules = SelectedFormalModules.ToList();
        if (modules.Count == 0 || _client is null) return;
        var skipped = SelectedItems.Count - modules.Count;

        var dialog = new SaveFileDialog
        {
            Title = "Modül içeriğini JSON olarak dışa aktar",
            Filter = "JSON dosyası (*.json)|*.json",
            FileName = $"{SafeFileName(modules.Count == 1 ? modules[0].Name : "doors_moduller")}_{DateTime.Now:yyyyMMdd_HHmm}.json",
        };
        if (dialog.ShowDialog() != true) return;

        IsBusy = true;
        Error = null;
        _operationCts = new CancellationTokenSource();
        var progress = new Progress<ModuleExportProgress>(p =>
            StatusText = $"Modül okunuyor ({p.Index}/{p.Total}): {p.ModuleFullName}…");
        try
        {
            var results = await ModuleJsonExporter.ReadModulesAsync(_client, modules, progress, _operationCts.Token);
            var failed = results.Where(r => !r.Succeeded).ToList();

            if (failed.Count == results.Count)
            {
                // Nothing useful to save: explain the (first) reason instead of writing an empty file.
                ReportError(results.Count == 1 ? "Modül dışa aktarılamadı" : "Modüller dışa aktarılamadı",
                    failed[0].Error!, retry: ExportJsonAsync);
                return;
            }

            await ModuleJsonExporter.WriteAsync(dialog.FileName, results, _session, DateTimeOffset.Now, _operationCts.Token);

            Warnings.Clear();
            foreach (var f in failed) Warnings.Add($"'{f.Module.FullName}' aktarılamadı: {f.Error!.Message}");
            foreach (var r in results.Where(r => r.Succeeded))
            {
                foreach (var w in r.Content!.Warnings) Warnings.Add($"'{r.Module.FullName}': {w}");
            }

            RefreshView();

            var objects = results.Sum(r => r.Content?.Objects.Count ?? 0);
            var summary = $"{results.Count - failed.Count} modül ({objects} nesne) dışa aktarıldı: {dialog.FileName}";
            if (failed.Count > 0) summary += $" · {failed.Count} modül aktarılamadı (ayrıntılar uyarılarda)";
            if (skipped > 0) summary += $" · {skipped} öğe formal modül olmadığı için atlandı";
            StatusText = summary;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Dışa aktarma iptal edildi; dosya yazılmadı.";
        }
        catch (Exception ex)
        {
            var lost = ex is DoorsException { Kind: DoorsErrorKind.ConnectionLost };
            if (lost)
            {
                Disconnect();
            }

            ReportError(lost ? "DOORS bağlantısı koptu" : "Modüller dışa aktarılamadı", ex, retry: ExportJsonAsync);
        }
        finally
        {
            _operationCts.Dispose();
            _operationCts = null;
            IsBusy = false;
        }
    }

    private static string SafeFileName(string name) =>
        string.Join("_", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)) is { Length: > 0 } s
            ? s
            : "doors";

    /// <summary>Repeats the operation that failed; reconnects first if the connection is gone.</summary>
    private Task RetryAsync()
    {
        if (!IsConnected) return ConnectAsync();
        return (_retry ?? ListAsync)();
    }

    private void ReportError(string title, Exception ex, string? rootPath = null, Func<Task>? retry = null)
    {
        _retry = retry;
        var context = new List<KeyValuePair<string, string>>
        {
            new("Bağlantı türü", SelectedMode.Label),
        };
        if (rootPath is not null) context.Add(new("Kök klasör", rootPath));
        context.AddRange(EnvironmentInfo.Describe());

        Error = DoorsErrorReport.Create(title, ex, context);
        StatusText = $"{title}: {Error.Reason}";
    }

    private void CopyError()
    {
        if (Error is null) return;
        try
        {
            Clipboard.SetText(Error.ToClipboardText());
            StatusText = "Hata ayrıntıları panoya kopyalandı.";
        }
        catch (Exception ex)
        {
            ShowError("Pano hatası", ex);
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
        var message = ex is InvalidOperationException ? ex.Message : $"Beklenmeyen hata: {ex.Message}";
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public void Dispose()
    {
        _operationCts?.Cancel();
        _client?.Dispose();
        _client = null;
    }
}
