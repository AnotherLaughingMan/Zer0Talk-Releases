using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Media;

namespace Zer0Talk.ViewModels;

public class ThemeHistoryViewModel : INotifyPropertyChanged
{
    private ObservableCollection<HistoryItem> _historyItems = new();
    
    public ObservableCollection<HistoryItem> HistoryItems
    {
        get => _historyItems;
        set
        {
            if (_historyItems != value)
            {
                _historyItems = value;
                OnPropertyChanged();
            }
        }
    }

    public void UpdateHistory(
        IEnumerable<ThemeEditorViewModel.ColorEditAction> undoStack, 
        IEnumerable<ThemeEditorViewModel.ColorEditAction> redoStack,
        int currentPosition)
    {
        var items = new List<HistoryItem>();
        var allActions = new List<(ThemeEditorViewModel.ColorEditAction action, bool isUndo)>();

        // Add undo stack items (in reverse order, oldest first)
        foreach (var action in undoStack.Reverse())
        {
            allActions.Add((action, true));
        }

        // Add redo stack items
        foreach (var action in redoStack)
        {
            allActions.Add((action, false));
        }

        // Create history items
        for (int i = 0; i < allActions.Count; i++)
        {
            var (action, isUndo) = allActions[i];
            var isCurrentState = (i == undoStack.Count() - 1);

            items.Add(new HistoryItem
            {
                Description = GetFriendlyName(action.ResourceKey),
                ColorChange = $"{action.OldValue} → {action.NewValue}",
                OldColor = action.OldValue,
                NewColor = action.NewValue,
                IsCurrentState = isCurrentState,
                IsFutureState = !isUndo && !isCurrentState
            });
        }

        HistoryItems = new ObservableCollection<HistoryItem>(items);
    }

    private string GetFriendlyName(string resourceKey)
    {
        // Convert resource key to friendly display name
        return resourceKey
            .Replace("App.", "")
            .Replace("SystemAccent", "Accent")
            .Replace("SystemList", "List")
            .Replace("SystemAlt", "Alt")
            .Replace("SystemBase", "Base")
            .Replace("SystemChrome", "Chrome")
            .Replace("Color", "");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class HistoryItem : INotifyPropertyChanged
    {
        private string _description = "";
        private string _colorChange = "";
        private string _oldColor = "";
        private string _newColor = "";
        private bool _isCurrentState;
        private bool _isFutureState;

        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        public string ColorChange
        {
            get => _colorChange;
            set { _colorChange = value; OnPropertyChanged(); }
        }

        public string OldColor
        {
            get => _oldColor;
            set { _oldColor = value; OnPropertyChanged(); OnPropertyChanged(nameof(OldColorBrush)); }
        }

        public string NewColor
        {
            get => _newColor;
            set { _newColor = value; OnPropertyChanged(); OnPropertyChanged(nameof(NewColorBrush)); }
        }

        public bool IsCurrentState
        {
            get => _isCurrentState;
            set
            {
                _isCurrentState = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Background));
                OnPropertyChanged(nameof(Foreground));
                OnPropertyChanged(nameof(FontWeight));
            }
        }

        public bool IsFutureState
        {
            get => _isFutureState;
            set
            {
                _isFutureState = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Foreground));
            }
        }

        public IBrush OldColorBrush
        {
            get
            {
                try
                {
                    return Brush.Parse(OldColor);
                }
                catch
                {
                    return Brushes.Transparent;
                }
            }
        }

        public IBrush NewColorBrush
        {
            get
            {
                try
                {
                    return Brush.Parse(NewColor);
                }
                catch
                {
                    return Brushes.Transparent;
                }
            }
        }

        public string Background => IsCurrentState ? "#2A5A8F" : "Transparent";
        public string Foreground => IsFutureState ? "#808080" : "#FFFFFF";
        public string FontWeight => IsCurrentState ? "Bold" : "Normal";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
