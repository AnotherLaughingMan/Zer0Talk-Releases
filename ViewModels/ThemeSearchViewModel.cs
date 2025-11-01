using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Zer0Talk.Services;

namespace Zer0Talk.ViewModels;

public class ThemeSearchViewModel : INotifyPropertyChanged, IDisposable
{
    private string _progressMessage = "Initializing search...";
    private string _statusInfo = "";
    private bool _isScanning = true;
    private CancellationTokenSource? _cancellationTokenSource;

    public string ProgressMessage
    {
        get => _progressMessage;
        set
        {
            if (_progressMessage != value)
            {
                _progressMessage = value;
                OnPropertyChanged();
            }
        }
    }

    public string StatusInfo
    {
        get => _statusInfo;
        set
        {
            if (_statusInfo != value)
            {
                _statusInfo = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsScanning
    {
        get => _isScanning;
        set
        {
            if (_isScanning != value)
            {
                _isScanning = value;
                OnPropertyChanged();
            }
        }
    }

    public ICommand CancelScanCommand { get; }

    public ThemeSearchViewModel()
    {
        CancelScanCommand = new RelayCommand(_ => CancelScan(), _ => IsScanning);
    }

    public async Task StartSearchAsync()
    {
        _cancellationTokenSource = new CancellationTokenSource();
        IsScanning = true;
        ProgressMessage = "Searching drives for theme files...";
        StatusInfo = "";

        try
        {
            var foundThemes = new System.Collections.Generic.List<string>();

            var progressHandler = new Action<string>(msg =>
            {
                ProgressMessage = msg;
            });

            foundThemes = await AppServices.ThemeEngine.SearchDrivesForThemesAsync(
                progressHandler, 
                _cancellationTokenSource.Token);

            if (_cancellationTokenSource.Token.IsCancellationRequested)
            {
                ProgressMessage = "Search cancelled";
                StatusInfo = "The search was cancelled by user";
                IsScanning = false;
                return;
            }

            if (foundThemes.Count == 0)
            {
                ProgressMessage = "Search complete";
                StatusInfo = "No theme files found";
                IsScanning = false;
                return;
            }

            ProgressMessage = $"Loading {foundThemes.Count} theme files...";
            var loaded = AppServices.ThemeEngine.LoadThemesFromPaths(foundThemes);

            ProgressMessage = "Search complete";
            StatusInfo = $"Successfully loaded {loaded} of {foundThemes.Count} themes";
            IsScanning = false;
        }
        catch (OperationCanceledException)
        {
            ProgressMessage = "Search cancelled";
            StatusInfo = "The search was cancelled by user";
            IsScanning = false;
        }
        catch (Exception ex)
        {
            ProgressMessage = "Search failed";
            StatusInfo = $"Error: {ex.Message}";
            IsScanning = false;
        }
    }

    private void CancelScan()
    {
        _cancellationTokenSource?.Cancel();
        ProgressMessage = "Cancelling search...";
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
