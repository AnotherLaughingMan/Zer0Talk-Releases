using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;

using ZTalk.Services;

namespace ZTalk.ViewModels;

public class LoadingWindowViewModel : INotifyPropertyChanged
{
    private string _mainMessage = "Getting things ready for you...";
    private string _currentTask = "Initializing...";
    private double _progress = 0;

    // Localized properties
    public string LocalizedTitle => AppServices.Localization.GetString("Loading.Title", "Loading...");
    public string LocalizedGettingReady => AppServices.Localization.GetString("Loading.GettingReady", "Getting things ready for you...");
    public string LocalizedInitializing => AppServices.Localization.GetString("Loading.Initializing", "Initializing...");
    public string LocalizedInitCrypto => AppServices.Localization.GetString("Loading.InitCrypto", "Initializing cryptography");
    public string LocalizedPreloadAudio => AppServices.Localization.GetString("Loading.PreloadAudio", "Preloading audio system");
    public string LocalizedLoadConfig => AppServices.Localization.GetString("Loading.LoadConfig", "Loading configuration");
    public string LocalizedApplyTheme => AppServices.Localization.GetString("Loading.ApplyTheme", "Applying theme settings");
    public string LocalizedPrepareUI => AppServices.Localization.GetString("Loading.PrepareUI", "Preparing user interface");
    public string LocalizedStartNetwork => AppServices.Localization.GetString("Loading.StartNetwork", "Starting network services");

    public string MainMessage
    {
        get => _mainMessage;
        set
        {
            _mainMessage = value;
            OnPropertyChanged();
        }
    }

    public string CurrentTask
    {
        get => _currentTask;
        set
        {
            _currentTask = value;
            OnPropertyChanged();
        }
    }

    public double Progress
    {
        get => _progress;
        set
        {
            _progress = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<LoadingStep> LoadingSteps { get; } = new();

    public LoadingWindowViewModel()
    {
        InitializeLoadingSteps();
        AppServices.Localization.LanguageChanged += RefreshLocalizedStrings;
    }

    private void RefreshLocalizedStrings()
    {
        OnPropertyChanged(nameof(LocalizedTitle));
        OnPropertyChanged(nameof(LocalizedGettingReady));
        OnPropertyChanged(nameof(LocalizedInitializing));
        OnPropertyChanged(nameof(LocalizedInitCrypto));
        OnPropertyChanged(nameof(LocalizedPreloadAudio));
        OnPropertyChanged(nameof(LocalizedLoadConfig));
        OnPropertyChanged(nameof(LocalizedApplyTheme));
        OnPropertyChanged(nameof(LocalizedPrepareUI));
        OnPropertyChanged(nameof(LocalizedStartNetwork));
    }

    private void InitializeLoadingSteps()
    {
        LoadingSteps.Add(new LoadingStep(LocalizedInitCrypto, LoadingStatus.Pending));
        LoadingSteps.Add(new LoadingStep(LocalizedPreloadAudio, LoadingStatus.Pending));
        LoadingSteps.Add(new LoadingStep(LocalizedLoadConfig, LoadingStatus.Pending));
        LoadingSteps.Add(new LoadingStep(LocalizedApplyTheme, LoadingStatus.Pending));
        LoadingSteps.Add(new LoadingStep(LocalizedPrepareUI, LoadingStatus.Pending));
        LoadingSteps.Add(new LoadingStep(LocalizedStartNetwork, LoadingStatus.Pending));
    }

    public void UpdateStep(string description, LoadingStatus status)
    {
        var step = LoadingSteps.FirstOrDefault(s => s.Description == description);
        if (step != null)
        {
            step.Status = status;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public enum LoadingStatus
{
    Pending,
    InProgress,
    Complete,
    Error
}

public class LoadingStep : INotifyPropertyChanged
{
    private LoadingStatus _status;

    public string Description { get; }

    public LoadingStatus Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Icon));
            OnPropertyChanged(nameof(StatusColor));
        }
    }

    public string Icon => Status switch
    {
        LoadingStatus.Pending => "⏳",
        LoadingStatus.InProgress => "🔄",
        LoadingStatus.Complete => "✅",
        LoadingStatus.Error => "❌",
        _ => "⏳"
    };

    public string StatusColor => Status switch
    {
        LoadingStatus.Pending => "#666666",
        LoadingStatus.InProgress => "#4a9eff",
        LoadingStatus.Complete => "#4ade80",
        LoadingStatus.Error => "#ef4444",
        _ => "#666666"
    };

    public LoadingStep(string description, LoadingStatus status = LoadingStatus.Pending)
    {
        Description = description;
        _status = status;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}