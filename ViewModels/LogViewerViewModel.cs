using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

using ZTalk.Utilities;

namespace ZTalk.ViewModels;

public sealed class LogViewerViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly RelayCommand _refreshCommand;
    private readonly RelayCommand _openFolderCommand;
    private readonly RelayCommand _copyPathCommand;
    private readonly RelayCommand _reloadCommand;
    private readonly RelayCommand _trimSelectedCommand;

    private LogDocumentViewModel? _selectedTab;
    private bool _isLoading;
    private string _statusMessage = string.Empty;
    private bool _autoscrollEnabled = true;

    public LogViewerViewModel()
    {
        Tabs.CollectionChanged += OnTabsCollectionChanged;
        _refreshCommand = new RelayCommand(async _ => await RefreshAsync());
        _openFolderCommand = new RelayCommand(_ => OpenFolder(), _ => Directory.Exists(CurrentDirectory));
        _copyPathCommand = new RelayCommand(async _ => await CopySelectedPathAsync(), _ => SelectedTab != null);
        _reloadCommand = new RelayCommand(async _ => await ReloadSelectedAsync(), _ => SelectedTab != null);
        _trimSelectedCommand = new RelayCommand(async _ => await TrimSelectedAsync(), _ => SelectedTab != null && File.Exists(SelectedTab.FullPath));
    }

    public ObservableCollection<LogDocumentViewModel> Tabs { get; } = new();

    public string CurrentDirectory => LoggingPaths.LogsDirectory;

    public bool HasTabs => Tabs.Count > 0;

    public bool AutoscrollEnabled
    {
        get => _autoscrollEnabled;
        set
        {
            if (_autoscrollEnabled == value) return;
            _autoscrollEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value) return;
            _isLoading = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value) return;
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public RelayCommand RefreshCommand => _refreshCommand;
    public RelayCommand OpenFolderCommand => _openFolderCommand;
    public RelayCommand CopyPathCommand => _copyPathCommand;
    public RelayCommand ReloadCommand => _reloadCommand;
    public RelayCommand TrimSelectedCommand => _trimSelectedCommand;

    public LogDocumentViewModel? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (_selectedTab == value) return;
            if (_selectedTab != null)
            {
                _selectedTab.AutoUpdated -= OnSelectedTabAutoUpdated;
                _selectedTab.StopWatching();
            }
            _selectedTab = value;
            OnPropertyChanged();
            _copyPathCommand.RaiseCanExecuteChanged();
            _reloadCommand.RaiseCanExecuteChanged();
            _trimSelectedCommand.RaiseCanExecuteChanged();
            if (_selectedTab != null)
            {
                _selectedTab.AutoUpdated += OnSelectedTabAutoUpdated;
                _selectedTab.StartWatching();
                _ = LoadSelectionAsync();
            }
        }
    }

    public async Task RefreshAsync()
    {
        if (IsLoading) return;

        StatusMessage = "Loading logs...";
        IsLoading = true;
        try
        {
            if (!LoggingPaths.Enabled)
            {
                SelectedTab = null;
                CleanupTabs();
                StatusMessage = "Logging is disabled.";
                return;
            }

            var dir = CurrentDirectory;
            if (!Directory.Exists(dir))
            {
                SelectedTab = null;
                CleanupTabs();
                StatusMessage = "Log directory not found.";
                return;
            }

            var files = await Task.Run(() => Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly)
                                                      .Where(IsLogFile)
                                                      .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                                                      .ToArray());

            var previous = SelectedTab?.FullPath;
            SelectedTab = null;
            CleanupTabs();
            foreach (var file in files)
            {
                var doc = new LogDocumentViewModel(file);
                doc.UpdateMetadata();
                Tabs.Add(doc);
            }

            if (Tabs.Count == 0)
            {
                SelectedTab = null;
                StatusMessage = "No log or text files found.";
                return;
            }

            var match = Tabs.FirstOrDefault(t => string.Equals(t.FullPath, previous, StringComparison.OrdinalIgnoreCase));
            SelectedTab = match ?? Tabs[0];

            if (SelectedTab != null)
            {
                StatusMessage = $"Loaded {Tabs.Count} file{(Tabs.Count == 1 ? string.Empty : "s")}. Viewing {SelectedTab.DisplayName}.";
            }
            else
            {
                StatusMessage = $"Loaded {Tabs.Count} file{(Tabs.Count == 1 ? string.Empty : "s")}.";
            }
        }
        catch (Exception ex)
        {
            SelectedTab = null;
            CleanupTabs();
            StatusMessage = $"Failed to enumerate logs: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            _openFolderCommand.RaiseCanExecuteChanged();
        }
    }

    private static bool IsLogFile(string path)
    {
        try
        {
            var ext = Path.GetExtension(path);
            if (string.IsNullOrWhiteSpace(ext)) return false;
            return ext.Equals(".log", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".txt", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async Task LoadSelectionAsync()
    {
        var tab = _selectedTab;
        if (tab == null) return;

        await tab.EnsureContentAsync();
        StatusMessage = $"Viewing {tab.DisplayName}.";
    }

    private void OnSelectedTabAutoUpdated(object? sender, EventArgs e)
    {
        if (sender is not LogDocumentViewModel doc) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (SelectedTab == doc)
            {
                StatusMessage = $"Auto-updated {doc.DisplayName} @ {DateTime.Now:HH:mm:ss}";
            }
        });
    }

    private void OpenFolder()
    {
        try
        {
            var dir = CurrentDirectory;
            if (!Directory.Exists(dir)) return;

            var startInfo = new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unable to open folder: {ex.Message}";
        }
    }

    private async Task CopySelectedPathAsync()
    {
        var tab = SelectedTab;
        if (tab == null) return;

        try
        {
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var clipboard = lifetime?.MainWindow?.Clipboard ?? lifetime?.Windows?.FirstOrDefault(w => w.IsActive)?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(tab.FullPath);
                StatusMessage = "Copied file path to clipboard.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Copy failed: {ex.Message}";
        }
    }

    private async Task ReloadSelectedAsync()
    {
        var tab = SelectedTab;
        if (tab == null) return;

        StatusMessage = $"Reloading {tab.DisplayName}...";
        await tab.ReloadAsync();
        StatusMessage = $"Viewing {tab.DisplayName}.";
    }

    private void OnTabsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasTabs));
    }

    private void CleanupTabs()
    {
        foreach (var doc in Tabs.ToArray())
        {
            doc.Dispose();
        }
        Tabs.Clear();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private async Task TrimSelectedAsync()
    {
        if (SelectedTab == null) return;
        
        try
        {
            StatusMessage = $"Trimming {SelectedTab.DisplayName}...";
            
            var result = await Task.Run(() => 
                ZTalk.Services.AppServices.LogMaintenance.TrimSingleLogFile(SelectedTab.FullPath, "log-viewer"));
            
            StatusMessage = result;
            
            // Reload the tab to show the trimmed content
            await Task.Delay(500); // Brief delay to let file operations complete
            await SelectedTab.ReloadAsync();
            
            // Clear status after a delay
            await Task.Delay(2000);
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Trim failed: {ex.Message}";
            await Task.Delay(3000);
            StatusMessage = string.Empty;
        }
    }

    public void Dispose()
    {
        SelectedTab = null;
        CleanupTabs();
        Tabs.CollectionChanged -= OnTabsCollectionChanged;
    }
}

public sealed class LogDocumentViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly string _displayName;
    private readonly string _fullPath;
    private long _sizeBytes;
    private DateTime? _lastWriteUtc;
    private string _content = string.Empty;
    private bool _isLoading;
    private DateTime? _loadedTimestampUtc;
    private bool _contentLoaded;
    private FileSystemWatcher? _watcher;
    private readonly object _watchGate = new();
    private readonly object _reloadGate = new();
    private bool _reloadScheduled;
    private bool _queuedReload;
    private DateTime _lastReloadUtc = DateTime.MinValue;
    private static readonly TimeSpan WatcherDebounce = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan WatcherThrottle = TimeSpan.FromMilliseconds(750);

    public LogDocumentViewModel(string fullPath)
    {
        _fullPath = fullPath;
        _displayName = Path.GetFileName(fullPath);
    }

    public event EventHandler? AutoUpdated;

    public string DisplayName => _displayName;
    public string FullPath => _fullPath;

    public long SizeBytes
    {
        get => _sizeBytes;
        private set
        {
            if (_sizeBytes == value) return;
            _sizeBytes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Info));
        }
    }

    public DateTime? LastWriteUtc
    {
        get => _lastWriteUtc;
        private set
        {
            if (_lastWriteUtc == value) return;
            _lastWriteUtc = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Info));
        }
    }

    public string Content
    {
        get => _content;
        private set
        {
            if (_content == value) return;
            _content = value;
            OnPropertyChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value) return;
            _isLoading = value;
            OnPropertyChanged();
        }
    }

    public string Info
    {
        get
        {
            var size = FormatSize(SizeBytes);
            var updated = LastWriteUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "n/a";
            return $"{size} | Updated {updated}";
        }
    }

    public void UpdateMetadata()
    {
        try
        {
            var info = new FileInfo(_fullPath);
            if (!info.Exists)
            {
                SizeBytes = 0;
                LastWriteUtc = null;
                _contentLoaded = false;
                return;
            }

            var writeUtc = info.LastWriteTimeUtc;
            SizeBytes = info.Length;
            LastWriteUtc = writeUtc;
            if (_loadedTimestampUtc != writeUtc)
            {
                _contentLoaded = false;
            }
        }
        catch
        {
            SizeBytes = 0;
            LastWriteUtc = null;
            _contentLoaded = false;
        }
    }

    public void StartWatching()
    {
        lock (_watchGate)
        {
            if (_watcher != null) return;
            var directory = Path.GetDirectoryName(_fullPath);
            var fileName = Path.GetFileName(_fullPath);
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName)) return;
            try
            {
                var watcher = new FileSystemWatcher(directory, fileName)
                {
                    NotifyFilter = NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.FileName,
                    IncludeSubdirectories = false
                };
                watcher.Changed += OnWatcherChanged;
                watcher.Created += OnWatcherChanged;
                watcher.Deleted += OnWatcherChanged;
                watcher.Renamed += OnWatcherRenamed;
                watcher.EnableRaisingEvents = true;
                _watcher = watcher;
            }
            catch
            {
                _watcher = null;
            }
        }
    }

    public void StopWatching()
    {
        lock (_watchGate)
        {
            if (_watcher == null) return;
            try
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Changed -= OnWatcherChanged;
                _watcher.Created -= OnWatcherChanged;
                _watcher.Deleted -= OnWatcherChanged;
                _watcher.Renamed -= OnWatcherRenamed;
                _watcher.Dispose();
            }
            catch
            {
            }
            finally
            {
                _watcher = null;
            }
        }
    }

    public Task EnsureContentAsync()
    {
        if (_contentLoaded) return Task.CompletedTask;
        return LoadFileAsync(fromWatcher: false);
    }

    public Task ReloadAsync(bool fromWatcher = false)
    {
        _contentLoaded = false;
        return LoadFileAsync(fromWatcher);
    }

    private void OnWatcherChanged(object? sender, FileSystemEventArgs e) => ScheduleReload();
    private void OnWatcherRenamed(object? sender, RenamedEventArgs e) => ScheduleReload();

    private void ScheduleReload()
    {
        lock (_reloadGate)
        {
            if (_reloadScheduled)
            {
                _queuedReload = true;
                return;
            }
            _reloadScheduled = true;
            _queuedReload = false;
        }

        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    var delay = WatcherDebounce;
                    var elapsed = DateTime.UtcNow - _lastReloadUtc;
                    if (elapsed < WatcherThrottle)
                    {
                        var remaining = WatcherThrottle - elapsed;
                        if (remaining > delay)
                            delay = remaining;
                    }

                    await Task.Delay(delay);
                    await ReloadAsync(fromWatcher: true);
                    _lastReloadUtc = DateTime.UtcNow;
                }
                catch
                {
                    _lastReloadUtc = DateTime.UtcNow;
                }

                lock (_reloadGate)
                {
                    if (_queuedReload)
                    {
                        _queuedReload = false;
                        continue;
                    }

                    _reloadScheduled = false;
                    break;
                }
            }
        });
    }

    private async Task LoadFileAsync(bool fromWatcher)
    {
        if (IsLoading) return;

        IsLoading = true;
        try
        {
            if (!File.Exists(_fullPath))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Content = "File not found.";
                    SizeBytes = 0;
                    LastWriteUtc = null;
                    _loadedTimestampUtc = null;
                    _contentLoaded = true;
                    if (fromWatcher) AutoUpdated?.Invoke(this, EventArgs.Empty);
                });
                return;
            }

            string text;
            long sizeBytes = 0;
            DateTime? lastWriteUtc = null;
            try
            {
                using var stream = new FileStream(_fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Encoding.UTF8, true);
                text = await reader.ReadToEndAsync();
                try
                {
                    var info = new FileInfo(_fullPath);
                    if (info.Exists)
                    {
                        sizeBytes = info.Length;
                        lastWriteUtc = info.LastWriteTimeUtc;
                    }
                }
                catch
                {
                    sizeBytes = 0;
                    lastWriteUtc = null;
                }
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Content = $"Failed to read log:{Environment.NewLine}{ex.Message}";
                    _contentLoaded = false;
                    if (fromWatcher) AutoUpdated?.Invoke(this, EventArgs.Empty);
                });
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Content = text;
                SizeBytes = sizeBytes;
                LastWriteUtc = lastWriteUtc;
                _loadedTimestampUtc = LastWriteUtc;
                _contentLoaded = true;
                if (fromWatcher) AutoUpdated?.Invoke(this, EventArgs.Empty);
            });
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        var units = new[] { "B", "KiB", "MiB", "GiB" };
        var idx = 0;
        double size = bytes;
        while (size >= 1024 && idx < units.Length - 1)
        {
            size /= 1024;
            idx++;
        }
        return idx == 0
            ? string.Format(CultureInfo.InvariantCulture, "{0:0} {1}", size, units[idx])
            : string.Format(CultureInfo.InvariantCulture, "{0:0.0} {1}", size, units[idx]);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        StopWatching();
    }
}
