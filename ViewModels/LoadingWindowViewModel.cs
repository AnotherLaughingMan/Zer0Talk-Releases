using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;

namespace ZTalk.ViewModels;

public class LoadingWindowViewModel : INotifyPropertyChanged
{
    private string _mainMessage = "Getting things ready for you...";
    private string _currentTask = "Initializing...";
    private double _progress = 0;
    
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
    }
    
    private void InitializeLoadingSteps()
    {
        LoadingSteps.Add(new LoadingStep("Initializing cryptography", LoadingStatus.Pending));
        LoadingSteps.Add(new LoadingStep("Preloading audio system", LoadingStatus.Pending));
        LoadingSteps.Add(new LoadingStep("Loading configuration", LoadingStatus.Pending));
        LoadingSteps.Add(new LoadingStep("Applying theme settings", LoadingStatus.Pending));
        LoadingSteps.Add(new LoadingStep("Preparing user interface", LoadingStatus.Pending));
        LoadingSteps.Add(new LoadingStep("Starting network services", LoadingStatus.Pending));
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