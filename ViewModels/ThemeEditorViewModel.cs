using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Controls;
using Zer0Talk.Models;
using Zer0Talk.Services;
using Zer0Talk.Utilities;

namespace Zer0Talk.ViewModels;

public class ThemeEditorViewModel : INotifyPropertyChanged
{
    #region Properties and Collections

    private System.Collections.ObjectModel.ObservableCollection<ThemeColorEntry> _themeColors = new();
    public System.Collections.ObjectModel.ObservableCollection<ThemeColorEntry> ThemeColors
    {
        get => _themeColors;
        set { _themeColors = value; OnPropertyChanged(); }
    }

    private System.Collections.ObjectModel.ObservableCollection<ThemeGradientEntry> _themeGradients = new();
    public System.Collections.ObjectModel.ObservableCollection<ThemeGradientEntry> ThemeGradients
    {
        get => _themeGradients;
        set { _themeGradients = value; OnPropertyChanged(); }
    }

    // Theme metadata
    private string _currentThemeId = string.Empty;
    public string CurrentThemeId
    {
        get => _currentThemeId;
        set { _currentThemeId = value; OnPropertyChanged(); }
    }

    private string _currentThemeDisplayName = string.Empty;
    public string CurrentThemeDisplayName
    {
        get => _currentThemeDisplayName;
        set { _currentThemeDisplayName = value; OnPropertyChanged(); }
    }

    private string _currentThemeDescription = string.Empty;
    public string CurrentThemeDescription
    {
        get => _currentThemeDescription;
        set { _currentThemeDescription = value; OnPropertyChanged(); }
    }

    private string _currentThemeVersion = string.Empty;
    public string CurrentThemeVersion
    {
        get => _currentThemeVersion;
        set { _currentThemeVersion = value; OnPropertyChanged(); }
    }

    private string _currentThemeAuthor = string.Empty;
    public string CurrentThemeAuthor
    {
        get => _currentThemeAuthor;
        set { _currentThemeAuthor = value; OnPropertyChanged(); }
    }

    private bool _currentThemeIsReadOnly;
    public bool CurrentThemeIsReadOnly
    {
        get => _currentThemeIsReadOnly;
        set { _currentThemeIsReadOnly = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentThemeIsEditable)); }
    }

    public bool CurrentThemeIsEditable => !_currentThemeIsReadOnly;

    private string _currentThemeBaseVariant = "Dark";
    public string CurrentThemeBaseVariant
    {
        get => _currentThemeBaseVariant;
        set
        {
            if (!string.Equals(_currentThemeBaseVariant, value, StringComparison.Ordinal))
            {
                _currentThemeBaseVariant = string.IsNullOrWhiteSpace(value) ? "Dark" : value;
                OnPropertyChanged();
            }
        }
    }

    private System.Collections.Generic.List<string> _currentThemeResourceDictionaries = new();
    public System.Collections.Generic.IReadOnlyList<string> CurrentThemeResourceDictionaries => _currentThemeResourceDictionaries;

    // Track the file path and creation time for the currently loaded theme
    private string? _currentThemeFilePath;
    private DateTime _currentThemeCreatedAt = DateTime.UtcNow;

    private string? _currentThemeDefaultFontFamily;
    public string? CurrentThemeDefaultFontFamily
    {
        get => _currentThemeDefaultFontFamily;
        set
        {
            if (_currentThemeDefaultFontFamily != value)
            {
                _currentThemeDefaultFontFamily = value;
                OnPropertyChanged();
            }
        }
    }

    private double _currentThemeDefaultUiScale = 1.0;
    public double CurrentThemeDefaultUiScale
    {
        get => _currentThemeDefaultUiScale;
        set
        {
            if (Math.Abs(_currentThemeDefaultUiScale - value) > 0.0001)
            {
                _currentThemeDefaultUiScale = value;
                OnPropertyChanged();
            }
        }
    }

    private System.Collections.Generic.List<string> _currentThemeTags = new();
    public System.Collections.Generic.IReadOnlyList<string> CurrentThemeTags => _currentThemeTags;

    private System.Collections.Generic.Dictionary<string, string> _currentThemeMetadata = new();
    public System.Collections.Generic.IReadOnlyDictionary<string, string> CurrentThemeMetadata => _currentThemeMetadata;

    private string? _currentThemeMinAppVersion;
    private bool _currentThemeAllowsCustomization = true;
    private bool _currentThemeIsLegacyTheme;
    private ThemeOption? _currentThemeLegacyOption;

    #endregion

    #region Editing State

    // Color picker editing state
    private enum ColorUpdateSource
    {
        None,
        Hex,
        Rgb,
        Alpha,
        Hsv
    }

    private bool _suppressEditingUpdates;
    private bool _editingHexIsValid;
    private string _editingHex = "#FFFFFFFF";
    private byte _editingAlpha = 255;
    private byte _editingRed = 255;
    private byte _editingGreen = 255;
    private byte _editingBlue = 255;
    private double _editingHue;
    private double _editingSaturation;
    private double _editingValue;

    private readonly System.Collections.Generic.Stack<ColorEditAction> _undoStack = new();
    private readonly System.Collections.Generic.Stack<ColorEditAction> _redoStack = new();
    private ThemeColorEntry? _currentlyEditingColor = null;
    
    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public bool IsEditingColor => _currentlyEditingColor != null || _editingGradientStartColorMode || _editingGradientEndColorMode;

    private bool _isBatchEditMode = false;
    public bool IsBatchEditMode
    {
        get => _isBatchEditMode;
        set
        {
            if (_isBatchEditMode != value)
            {
                _isBatchEditMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotBatchEditMode));
            }
        }
    }
    public bool IsNotBatchEditMode => !_isBatchEditMode;

    private readonly System.Collections.ObjectModel.ObservableCollection<string> _recentColors = new();
    public System.Collections.ObjectModel.ObservableCollection<string> RecentColors => _recentColors;

    private string? _copiedColor = null;
    public bool HasCopiedColor => !string.IsNullOrEmpty(_copiedColor);

    public int SelectedColorCount => ThemeColors.Count(c => c.IsSelected);
    public bool HasSelectedColors => SelectedColorCount > 0;

    private ThemeGradientEntry? _currentlyEditingGradient = null;
    public bool IsEditingGradient => _currentlyEditingGradient != null;

    private readonly System.Collections.Generic.List<GradientPreset> _gradientPresets = new()
    {
        new GradientPreset { Name = "Sunset", StartColor = "#FF6B6B", EndColor = "#FFD93D", Angle = 135 },
        new GradientPreset { Name = "Ocean", StartColor = "#4FACFE", EndColor = "#00F2FE", Angle = 180 },
        new GradientPreset { Name = "Forest", StartColor = "#38EF7D", EndColor = "#11998E", Angle = 90 },
        new GradientPreset { Name = "Purple Haze", StartColor = "#A18CD1", EndColor = "#FBC2EB", Angle = 45 },
        new GradientPreset { Name = "Fire", StartColor = "#FF0844", EndColor = "#FFBC0D", Angle = 0 },
        new GradientPreset { Name = "Ice", StartColor = "#E0EAFC", EndColor = "#CFDEF3", Angle = 270 }
    };

    public System.Collections.Generic.IReadOnlyList<GradientPreset> GradientPresets => _gradientPresets;

    private GradientPreset? _selectedGradientPreset;
    public GradientPreset? SelectedGradientPreset
    {
        get => _selectedGradientPreset;
        set
        {
            if (_selectedGradientPreset != value)
            {
                _selectedGradientPreset = value;
                OnPropertyChanged();
                if (value != null)
                {
                    ApplyGradientPreset(value);
                }
            }
        }
    }

    // Gradient editing properties with notifications
    private string _editingGradientStartColor = "#000000";
    public string EditingGradientStartColor
    {
        get => _editingGradientStartColor;
        set
        {
            if (_editingGradientStartColor != value)
            {
                _editingGradientStartColor = value;
                OnPropertyChanged();
                if (_currentlyEditingGradient?.GradientDefinition != null)
                {
                    _currentlyEditingGradient.GradientDefinition.StartColor = value;
                }
            }
        }
    }

    private bool _editingGradientStartColorMode = false;
    private bool _editingGradientEndColorMode = false;

    private string _editingGradientEndColor = "#FFFFFF";
    public string EditingGradientEndColor
    {
        get => _editingGradientEndColor;
        set
        {
            if (_editingGradientEndColor != value)
            {
                _editingGradientEndColor = value;
                OnPropertyChanged();
                if (_currentlyEditingGradient?.GradientDefinition != null)
                {
                    _currentlyEditingGradient.GradientDefinition.EndColor = value;
                }
            }
        }
    }

    private double _editingGradientAngle = 0.0;
    public double EditingGradientAngle
    {
        get => _editingGradientAngle;
        set
        {
            if (Math.Abs(_editingGradientAngle - value) > 0.01)
            {
                _editingGradientAngle = value;
                OnPropertyChanged();
                if (_currentlyEditingGradient?.GradientDefinition != null)
                {
                    _currentlyEditingGradient.GradientDefinition.Angle = value;
                }
            }
        }
    }

    private bool _isEditingMetadata = false;
    public bool IsEditingMetadata
    {
        get => _isEditingMetadata;
        set
        {
            if (_isEditingMetadata != value)
            {
                _isEditingMetadata = value;
                OnPropertyChanged();
                
                // Trigger command re-evaluation for IsEditingMetadata-dependent commands
                (SaveMetadataCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (CancelMetadataEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (EditMetadataCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (EditColorCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (EditGradientCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (NewFromBlankTemplateCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (SaveAsCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    private string _editableThemeName = string.Empty;
    public string EditableThemeName
    {
        get => _editableThemeName;
        set { _editableThemeName = value; OnPropertyChanged(); }
    }

    private string _editableThemeDescription = string.Empty;
    public string EditableThemeDescription
    {
        get => _editableThemeDescription;
        set { _editableThemeDescription = value; OnPropertyChanged(); }
    }

    private string _editableThemeAuthor = string.Empty;
    public string EditableThemeAuthor
    {
        get => _editableThemeAuthor;
        set { _editableThemeAuthor = value; OnPropertyChanged(); }
    }

    private string _editableThemeVersion = string.Empty;
    public string EditableThemeVersion
    {
        get => _editableThemeVersion;
        set { _editableThemeVersion = value; OnPropertyChanged(); }
    }

    #endregion

    #region Color Picker State

    public string EditingHex
    {
        get => _editingHex;
        set
        {
            if (_editingHex == value) return;
            _editingHex = value;
            OnPropertyChanged();
            UpdateEditingColorFrom(ColorUpdateSource.Hex);
        }
    }

    public bool EditingHexIsValid
    {
        get => _editingHexIsValid;
        private set
        {
            if (_editingHexIsValid == value) return;
            _editingHexIsValid = value;
            OnPropertyChanged();
        }
    }

    public double EditingAlpha
    {
        get => _editingAlpha;
        set
        {
            var clamped = (byte)Math.Clamp(Math.Round(value), 0, 255);
            if (_editingAlpha == clamped) return;
            _editingAlpha = clamped;
            OnPropertyChanged();
            UpdateEditingColorFrom(ColorUpdateSource.Alpha);
        }
    }

    public double EditingRed
    {
        get => _editingRed;
        set
        {
            var clamped = (byte)Math.Clamp(Math.Round(value), 0, 255);
            if (_editingRed == clamped) return;
            _editingRed = clamped;
            OnPropertyChanged();
            UpdateEditingColorFrom(ColorUpdateSource.Rgb);
        }
    }

    public double EditingGreen
    {
        get => _editingGreen;
        set
        {
            var clamped = (byte)Math.Clamp(Math.Round(value), 0, 255);
            if (_editingGreen == clamped) return;
            _editingGreen = clamped;
            OnPropertyChanged();
            UpdateEditingColorFrom(ColorUpdateSource.Rgb);
        }
    }

    public double EditingBlue
    {
        get => _editingBlue;
        set
        {
            var clamped = (byte)Math.Clamp(Math.Round(value), 0, 255);
            if (_editingBlue == clamped) return;
            _editingBlue = clamped;
            OnPropertyChanged();
            UpdateEditingColorFrom(ColorUpdateSource.Rgb);
        }
    }

    public double EditingHue
    {
        get => _editingHue;
        set
        {
            var normalized = NormalizeHue(value);
            if (NearlyEqual(_editingHue, normalized)) return;
            _editingHue = normalized;
            OnPropertyChanged();
            UpdateEditingColorFrom(ColorUpdateSource.Hsv);
        }
    }

    public double EditingSaturation
    {
        get => _editingSaturation;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (NearlyEqual(_editingSaturation, clamped)) return;
            _editingSaturation = clamped;
            OnPropertyChanged();
            UpdateEditingColorFrom(ColorUpdateSource.Hsv);
        }
    }

    public double EditingValue
    {
        get => _editingValue;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (NearlyEqual(_editingValue, clamped)) return;
            _editingValue = clamped;
            OnPropertyChanged();
            UpdateEditingColorFrom(ColorUpdateSource.Hsv);
        }
    }

    public Color EditingColor => Color.FromArgb(_editingAlpha, _editingRed, _editingGreen, _editingBlue);

    #endregion

    #region Commands

    public ICommand EditColorCommand { get; }
    public ICommand SaveColorEditCommand { get; }
    public ICommand CancelColorEditCommand { get; }
    public ICommand UndoColorEditCommand { get; }
    public ICommand RedoColorEditCommand { get; }
    public ICommand ToggleBatchEditModeCommand { get; }
    public ICommand SelectAllColorsCommand { get; }
    public ICommand DeselectAllColorsCommand { get; }
    public ICommand CopyColorCommand { get; }
    public ICommand PasteColorCommand { get; }
    public ICommand EditGradientCommand { get; }
    public ICommand SaveGradientEditCommand { get; }
    public ICommand CancelGradientEditCommand { get; }
    public ICommand ApplyGradientPresetCommand { get; }
    public ICommand ClearGradientCommand { get; }
    public ICommand EditMetadataCommand { get; }
    public ICommand SaveMetadataCommand { get; }
    public ICommand CancelMetadataEditCommand { get; }
    public ICommand NewFromBlankTemplateCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand ImportThemeCommand { get; }
    public ICommand SearchDrivesForThemesCommand { get; }
    public ICommand ExportModifiedThemeCommand { get; }

    #endregion

    #region Constructor

    public ThemeEditorViewModel()
    {
        // Initialize commands
        EditColorCommand = new RelayCommand(param => StartEditingColor(param as ThemeColorEntry), param => param is ThemeColorEntry && !IsEditingColor);
        SaveColorEditCommand = new RelayCommand(async _ => await SaveColorEditAsync(), _ => IsEditingColor);
        CancelColorEditCommand = new RelayCommand(_ => CancelColorEdit(), _ => IsEditingColor);
        UndoColorEditCommand = new RelayCommand(_ => UndoColorEdit(), _ => CanUndo && !IsEditingColor);
        RedoColorEditCommand = new RelayCommand(_ => RedoColorEdit(), _ => CanRedo && !IsEditingColor);
        ToggleBatchEditModeCommand = new RelayCommand(_ => ToggleBatchEditMode(), _ => !IsEditingColor);
        SelectAllColorsCommand = new RelayCommand(_ => SelectAllColors(), _ => IsBatchEditMode);
        DeselectAllColorsCommand = new RelayCommand(_ => DeselectAllColors(), _ => IsBatchEditMode && HasSelectedColors);
        CopyColorCommand = new RelayCommand(param => CopyColor(param as ThemeColorEntry), param => param is ThemeColorEntry);
        PasteColorCommand = new RelayCommand(param => PasteColor(param as ThemeColorEntry), param => param is ThemeColorEntry && HasCopiedColor);
        EditGradientCommand = new RelayCommand(param => StartEditingGradient(param as ThemeGradientEntry), param => param is ThemeGradientEntry && !IsEditingGradient && !IsEditingColor);
        SaveGradientEditCommand = new RelayCommand(async _ => await SaveGradientEditAsync(), _ => IsEditingGradient);
        CancelGradientEditCommand = new RelayCommand(_ => CancelGradientEdit(), _ => IsEditingGradient);
        ApplyGradientPresetCommand = new RelayCommand(param => ApplyGradientPreset(param as GradientPreset), param => param is GradientPreset && IsEditingGradient);
        ClearGradientCommand = new RelayCommand(_ => ClearGradient(), _ => IsEditingGradient);
        EditMetadataCommand = new RelayCommand(_ => StartEditingMetadata(), _ => !IsEditingColor && !IsEditingGradient && !IsEditingMetadata);
        SaveMetadataCommand = new RelayCommand(async _ => await SaveMetadataAsync(), _ => IsEditingMetadata);
        CancelMetadataEditCommand = new RelayCommand(_ => CancelMetadataEdit(), _ => IsEditingMetadata);
        NewFromBlankTemplateCommand = new RelayCommand(async _ => await NewFromBlankTemplateAsync(), _ => !IsEditingColor && !IsEditingGradient && !IsEditingMetadata);
        SaveCommand = new RelayCommand(async _ => await SaveAsync(), _ => !string.IsNullOrEmpty(_currentThemeFilePath) && !IsEditingColor && !IsEditingGradient && !IsEditingMetadata);
        SaveAsCommand = new RelayCommand(async _ => await SaveAsAsync(), _ => true); // Always enabled - you can always SaveAs
        ImportThemeCommand = new RelayCommand(async _ => await ImportThemeAsync(), _ => true);
        SearchDrivesForThemesCommand = new RelayCommand(async _ => await SearchDrivesForThemesAsync(), _ => true);
        ExportModifiedThemeCommand = new RelayCommand(async _ => await ExportModifiedThemeAsync(), _ => CanUndo && !IsEditingColor && !IsEditingGradient);
    }

    #endregion

    #region Color Editing Methods

    private async void StartEditingColor(ThemeColorEntry? entry)
    {
        if (entry == null || _currentlyEditingColor != null) return;
        // Prefer opening a modal color picker dialog so editing is explicit and blocking.
        try
        {
            var dialog = new Zer0Talk.Controls.ColorPicker.ColorPickerDialog();
            // Initialize dialog from the current color
            InitializeEditingState(entry.ColorValue);
            
            // Set initial values from the current color
            dialog.SvPicker.Hue = EditingHue;
            dialog.SvPicker.Saturation = EditingSaturation;
            dialog.SvPicker.Value = EditingValue;
            dialog.HueSlider.Hue = EditingHue;
            dialog.BrightnessSlider.Brightness = EditingValue;
            dialog.RedSlider.Value = (byte)EditingRed;
            dialog.GreenSlider.Value = (byte)EditingGreen;
            dialog.BlueSlider.Value = (byte)EditingBlue;
            dialog.HexInput.Text = EditingHex;

            // Find owner window if running as a desktop app
            Window? owner = null;
            if (App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                owner = desktop.MainWindow;
            }

            var result = await dialog.ShowDialog<bool?>(owner!);

            // If user applied the selection, update the entry and save
            if (result == true)
            {
                // Pull HSV from dialog and update editing state
                var (h, s, v) = dialog.GetHsv();
                EditingHue = h;
                EditingSaturation = s;
                EditingValue = v;
                // Convert to ARGB via helper
                var newColor = ColorFromHsv(EditingHue, EditingSaturation, EditingValue, (byte)EditingAlpha);
                entry.ColorValue = ColorToHex(newColor);
                // Save
                _ = SaveColorEditAsync();
            }

            Logger.Log($"[Theme Edit] Modal color picker closed for '{entry.ResourceKey}' (applied: {result})", LogLevel.Info, categoryOverride: "theme");
        }
        catch (Exception ex)
        {
            // Fallback to inline editing if dialog cannot be shown
            _currentlyEditingColor = entry;
            entry.IsEditing = true;
            entry.OriginalValue = entry.ColorValue;
            InitializeEditingState(entry.ColorValue);
            OnPropertyChanged(nameof(IsEditingColor));

            Logger.Log($"[Theme Edit] Started editing color '{entry.ResourceKey}' (current: {entry.ColorValue}) - inline fallback due to: {ex.Message}", LogLevel.Info, categoryOverride: "theme");
        }
    }

    private async Task SaveColorEditAsync()
    {
        if (_currentlyEditingColor == null && !_editingGradientStartColorMode && !_editingGradientEndColorMode) return;

        if (_editingGradientStartColorMode)
        {
            // Save gradient start color
            EditingGradientStartColor = ColorToHex(EditingColor);
            _editingGradientStartColorMode = false;
            OnPropertyChanged(nameof(IsEditingColor));
            Logger.Log($"[Theme Edit] Updated gradient start color to {EditingGradientStartColor}", LogLevel.Info, categoryOverride: "theme");
            return;
        }

        if (_editingGradientEndColorMode)
        {
            // Save gradient end color
            EditingGradientEndColor = ColorToHex(EditingColor);
            _editingGradientEndColorMode = false;
            OnPropertyChanged(nameof(IsEditingColor));
            Logger.Log($"[Theme Edit] Updated gradient end color to {EditingGradientEndColor}", LogLevel.Info, categoryOverride: "theme");
            return;
        }

        var entry = _currentlyEditingColor;
        var oldValue = entry!.OriginalValue ?? entry.ColorValue;
        var newValue = entry.ColorValue;

        if (!ThemeDefinition.IsValidColorPublic(newValue))
        {
            Logger.Log($"[Theme Edit] Invalid color format: {newValue}", LogLevel.Warning, categoryOverride: "theme");
            return;
        }

        if (oldValue != newValue)
        {
            _undoStack.Push(new ColorEditAction
            {
                ResourceKey = entry.ResourceKey,
                OldValue = oldValue,
                NewValue = newValue
            });
            _redoStack.Clear();

            AddToRecentColors(newValue);
            Logger.Log($"[Theme Edit] Saved color edit '{entry.ResourceKey}': {oldValue} → {newValue}", LogLevel.Info, categoryOverride: "theme");
            
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));

            // Apply live preview of the color change
            try
            {
                var tempTheme = BuildThemeDefinition(preserveId: true);
                if (AppServices.ThemeEngine.ApplyThemePreview(tempTheme))
                {
                    Logger.Log($"[Theme Edit] Applied live preview for color '{entry.ResourceKey}'", LogLevel.Info, categoryOverride: "theme");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[Theme Edit] Failed to apply live preview: {ex.Message}", LogLevel.Warning, categoryOverride: "theme");
            }
        }

        entry.IsEditing = false;
        _currentlyEditingColor = null;
        OnPropertyChanged(nameof(IsEditingColor));
        
        // Trigger command re-evaluation for IsEditingColor-dependent commands
        (SaveColorEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CancelColorEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (EditColorCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (UndoColorEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RedoColorEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ToggleBatchEditModeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (EditGradientCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (EditMetadataCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (NewFromBlankTemplateCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SaveAsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ExportModifiedThemeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        
        await Task.CompletedTask;
    }

    private void CancelColorEdit()
    {
        if (_editingGradientStartColorMode)
        {
            _editingGradientStartColorMode = false;
            OnPropertyChanged(nameof(IsEditingColor));
            Logger.Log("[Theme Edit] Cancelled gradient start color editing", LogLevel.Info, categoryOverride: "theme");
            return;
        }

        if (_editingGradientEndColorMode)
        {
            _editingGradientEndColorMode = false;
            OnPropertyChanged(nameof(IsEditingColor));
            Logger.Log("[Theme Edit] Cancelled gradient end color editing", LogLevel.Info, categoryOverride: "theme");
            return;
        }

        if (_currentlyEditingColor == null) return;

        var entry = _currentlyEditingColor;
        entry.ColorValue = entry.OriginalValue ?? entry.ColorValue;
        entry.IsEditing = false;
        InitializeEditingState(entry.ColorValue);
        
        _currentlyEditingColor = null;
        OnPropertyChanged(nameof(IsEditingColor));
        
        // Trigger command re-evaluation for IsEditingColor-dependent commands
        (SaveColorEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CancelColorEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (EditColorCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (UndoColorEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RedoColorEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ToggleBatchEditModeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (EditGradientCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (EditMetadataCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (NewFromBlankTemplateCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SaveAsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ExportModifiedThemeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        
        Logger.Log($"[Theme Edit] Cancelled editing color '{entry.ResourceKey}'", LogLevel.Info, categoryOverride: "theme");
    }

    private void UndoColorEdit()
    {
        if (_undoStack.Count == 0) return;

        var action = _undoStack.Pop();
        _redoStack.Push(action);

        var entry = ThemeColors.FirstOrDefault(c => c.ResourceKey == action.ResourceKey);
        if (entry != null)
        {
            entry.ColorValue = action.OldValue;
            Logger.Log($"[Theme Edit] Undo: {action.ResourceKey} restored to {action.OldValue}", LogLevel.Info, categoryOverride: "theme");
        }

        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private void RedoColorEdit()
    {
        if (_redoStack.Count == 0) return;

        var action = _redoStack.Pop();
        _undoStack.Push(action);

        var entry = ThemeColors.FirstOrDefault(c => c.ResourceKey == action.ResourceKey);
        if (entry != null)
        {
            entry.ColorValue = action.NewValue;
            Logger.Log($"[Theme Edit] Redo: {action.ResourceKey} changed to {action.NewValue}", LogLevel.Info, categoryOverride: "theme");
        }

        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    #region Color Picker Helpers

    private void InitializeEditingState(string colorValue)
    {
        if (string.IsNullOrWhiteSpace(colorValue))
        {
            colorValue = "#FFFFFFFF";
        }

        if (!TryParseColor(colorValue, out var color))
        {
            color = Color.FromArgb(255, 255, 255, 255);
            EditingHexIsValid = false;
        }
        else
        {
            EditingHexIsValid = true;
        }

        _suppressEditingUpdates = true;
        try
        {
            _editingAlpha = color.A;
            _editingRed = color.R;
            _editingGreen = color.G;
            _editingBlue = color.B;
            ColorToHsv(color, out _editingHue, out _editingSaturation, out _editingValue);
            _editingHex = ColorToHex(color);
        }
        finally
        {
            _suppressEditingUpdates = false;
        }

        RaiseEditingStateChanged();
    }

    private void UpdateEditingColorFrom(ColorUpdateSource source)
    {
        if (_suppressEditingUpdates || source == ColorUpdateSource.None)
            return;

        bool invalidHex = false;

        _suppressEditingUpdates = true;
        try
        {
            switch (source)
            {
                case ColorUpdateSource.Hex:
                    if (!TryParseColor(_editingHex, out var hexColor))
                    {
                        invalidHex = true;
                        break;
                    }

                    EditingHexIsValid = true;
                    _editingAlpha = hexColor.A;
                    _editingRed = hexColor.R;
                    _editingGreen = hexColor.G;
                    _editingBlue = hexColor.B;
                    ColorToHsv(hexColor, out _editingHue, out _editingSaturation, out _editingValue);
                    break;

                case ColorUpdateSource.Rgb:
                case ColorUpdateSource.Alpha:
                {
                    var rgbColor = Color.FromArgb(_editingAlpha, _editingRed, _editingGreen, _editingBlue);
                    ColorToHsv(rgbColor, out _editingHue, out _editingSaturation, out _editingValue);
                    _editingHex = ColorToHex(rgbColor);
                    EditingHexIsValid = true;
                    break;
                }

                case ColorUpdateSource.Hsv:
                {
                    var hsvColor = ColorFromHsv(_editingHue, _editingSaturation, _editingValue, _editingAlpha);
                    _editingRed = hsvColor.R;
                    _editingGreen = hsvColor.G;
                    _editingBlue = hsvColor.B;
                    _editingHex = ColorToHex(hsvColor);
                    EditingHexIsValid = true;
                    break;
                }
            }
        }
        finally
        {
            _suppressEditingUpdates = false;
        }

        if (invalidHex)
        {
            EditingHexIsValid = false;
            return;
        }

        RaiseEditingStateChanged();
    }

    private void RaiseEditingStateChanged()
    {
        OnPropertyChanged(nameof(EditingColor));
        OnPropertyChanged(nameof(EditingAlpha));
        OnPropertyChanged(nameof(EditingRed));
        OnPropertyChanged(nameof(EditingGreen));
        OnPropertyChanged(nameof(EditingBlue));
        OnPropertyChanged(nameof(EditingHue));
        OnPropertyChanged(nameof(EditingSaturation));
        OnPropertyChanged(nameof(EditingValue));
        OnPropertyChanged(nameof(EditingHex));
        OnPropertyChanged(nameof(EditingHexIsValid));
    }

    private static string ColorToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static bool TryParseColor(string hex, out Color color)
    {
        color = default;

        if (string.IsNullOrWhiteSpace(hex) || !ThemeDefinition.IsValidColorPublic(hex))
            return false;

        var raw = hex[1..];
        if (raw.Length == 3)
        {
            raw = string.Concat(raw.Select(c => new string(c, 2)));
        }
        else if (raw.Length == 4)
        {
            var expanded = new char[8];
            for (var i = 0; i < 4; i++)
            {
                expanded[i * 2] = raw[i];
                expanded[i * 2 + 1] = raw[i];
            }
            raw = new string(expanded);
        }

        if (raw.Length == 6)
        {
            raw = "FF" + raw;
        }

        if (raw.Length != 8)
            return false;

        if (!byte.TryParse(raw[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a) ||
            !byte.TryParse(raw[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(raw[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(raw[6..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        color = Color.FromArgb(a, r, g, b);
        return true;
    }

    private static void ColorToHsv(Color color, out double h, out double s, out double v)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        if (delta < double.Epsilon)
        {
            h = 0;
        }
        else if (Math.Abs(max - r) < double.Epsilon)
        {
            h = 60 * (((g - b) / delta) % 6);
        }
        else if (Math.Abs(max - g) < double.Epsilon)
        {
            h = 60 * (((b - r) / delta) + 2);
        }
        else
        {
            h = 60 * (((r - g) / delta) + 4);
        }

        if (h < 0)
        {
            h += 360;
        }

        s = max <= 0 ? 0 : delta / max;
        v = max;
        h = NormalizeHue(h);
    }

    private static Color ColorFromHsv(double hue, double saturation, double value, byte alpha)
    {
        hue = NormalizeHue(hue);
        saturation = Math.Clamp(saturation, 0.0, 1.0);
        value = Math.Clamp(value, 0.0, 1.0);

        var c = value * saturation;
        var x = c * (1 - Math.Abs(((hue / 60.0) % 2) - 1));
        var m = value - c;

        double r1, g1, b1;

        if (hue < 60)
        {
            r1 = c; g1 = x; b1 = 0;
        }
        else if (hue < 120)
        {
            r1 = x; g1 = c; b1 = 0;
        }
        else if (hue < 180)
        {
            r1 = 0; g1 = c; b1 = x;
        }
        else if (hue < 240)
        {
            r1 = 0; g1 = x; b1 = c;
        }
        else if (hue < 300)
        {
            r1 = x; g1 = 0; b1 = c;
        }
        else
        {
            r1 = c; g1 = 0; b1 = x;
        }

        var r = (byte)Math.Clamp(Math.Round((r1 + m) * 255), 0, 255);
        var g = (byte)Math.Clamp(Math.Round((g1 + m) * 255), 0, 255);
        var b = (byte)Math.Clamp(Math.Round((b1 + m) * 255), 0, 255);

        return Color.FromArgb(alpha, r, g, b);
    }

    private static bool NearlyEqual(double a, double b, double epsilon = 1e-4)
    {
        return Math.Abs(a - b) <= epsilon;
    }

    private static double NormalizeHue(double hue)
    {
        if (double.IsNaN(hue) || double.IsInfinity(hue))
            return 0;

        var normalized = hue % 360.0;
        if (normalized < 0)
        {
            normalized += 360.0;
        }

        return normalized;
    }

    #endregion

    private void ToggleBatchEditMode()
    {
        IsBatchEditMode = !IsBatchEditMode;
        
        if (!IsBatchEditMode)
        {
            DeselectAllColors();
        }
        
        Logger.Log($"[Theme Edit] Batch edit mode: {(IsBatchEditMode ? "ON" : "OFF")}", LogLevel.Info, categoryOverride: "theme");
    }

    private void SelectAllColors()
    {
        foreach (var color in ThemeColors)
        {
            color.IsSelected = true;
        }
        OnPropertyChanged(nameof(SelectedColorCount));
        OnPropertyChanged(nameof(HasSelectedColors));
    }

    private void DeselectAllColors()
    {
        foreach (var color in ThemeColors)
        {
            color.IsSelected = false;
        }
        OnPropertyChanged(nameof(SelectedColorCount));
        OnPropertyChanged(nameof(HasSelectedColors));
    }

    private void CopyColor(ThemeColorEntry? entry)
    {
        if (entry == null) return;

        _copiedColor = entry.ColorValue;
        OnPropertyChanged(nameof(HasCopiedColor));
        
        Logger.Log($"[Theme Edit] Copied color '{entry.ResourceKey}': {entry.ColorValue}", LogLevel.Info, categoryOverride: "theme");
    }

    private void PasteColor(ThemeColorEntry? entry)
    {
        if (entry == null || string.IsNullOrEmpty(_copiedColor)) return;

        var oldValue = entry.ColorValue;
        var newValue = _copiedColor;

        if (oldValue != newValue)
        {
            _undoStack.Push(new ColorEditAction
            {
                ResourceKey = entry.ResourceKey,
                OldValue = oldValue,
                NewValue = newValue
            });
            _redoStack.Clear();

            entry.ColorValue = newValue;
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            
            Logger.Log($"[Theme Edit] Pasted color to '{entry.ResourceKey}': {oldValue} → {newValue}", LogLevel.Info, categoryOverride: "theme");
        }
    }

    private void AddToRecentColors(string color)
    {
        if (string.IsNullOrWhiteSpace(color)) return;

        _recentColors.Remove(color);
        _recentColors.Insert(0, color);
        
        while (_recentColors.Count > 10)
        {
            _recentColors.RemoveAt(_recentColors.Count - 1);
        }
    }

    #endregion

    #region Gradient Editing Methods

    private void StartEditingGradient(ThemeGradientEntry? entry)
    {
        if (entry == null || _currentlyEditingGradient != null || entry.GradientDefinition == null) return;

        _currentlyEditingGradient = entry;
        entry.IsEditing = true;
        
        entry.OriginalStartColor = entry.GradientDefinition.StartColor;
        entry.OriginalEndColor = entry.GradientDefinition.EndColor;
        entry.OriginalAngle = entry.GradientDefinition.Angle;
        
        // Initialize editing properties
        EditingGradientStartColor = entry.GradientDefinition.StartColor;
        EditingGradientEndColor = entry.GradientDefinition.EndColor;
        EditingGradientAngle = entry.GradientDefinition.Angle;
        
        OnPropertyChanged(nameof(IsEditingGradient));
        
        // Trigger command re-evaluation for IsEditingGradient-dependent commands
        (SaveGradientEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CancelGradientEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (EditGradientCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (EditColorCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (EditMetadataCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (NewFromBlankTemplateCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SaveAsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ExportModifiedThemeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        
        Logger.Log($"[Theme Edit] Started editing gradient '{entry.ResourceKey}' (angle: {entry.GradientDefinition.Angle}°)", LogLevel.Info, categoryOverride: "theme");
    }

    private async Task SaveGradientEditAsync()
    {
        if (_currentlyEditingGradient == null || _currentlyEditingGradient.GradientDefinition == null) return;

        var entry = _currentlyEditingGradient;
        var gradient = entry.GradientDefinition;

        if (!ThemeDefinition.IsValidColorPublic(gradient.StartColor))
        {
            Logger.Log($"[Theme Edit] Invalid start color format: {gradient.StartColor}", LogLevel.Warning, categoryOverride: "theme");
            return;
        }

        if (!ThemeDefinition.IsValidColorPublic(gradient.EndColor))
        {
            Logger.Log($"[Theme Edit] Invalid end color format: {gradient.EndColor}", LogLevel.Warning, categoryOverride: "theme");
            return;
        }

        if (gradient.Angle < 0 || gradient.Angle > 360)
        {
            Logger.Log($"[Theme Edit] Angle must be between 0 and 360 degrees", LogLevel.Warning, categoryOverride: "theme");
            return;
        }

        var changed = entry.OriginalStartColor != gradient.StartColor ||
                      entry.OriginalEndColor != gradient.EndColor ||
                      entry.OriginalAngle != gradient.Angle;

        if (changed)
        {
            Logger.Log($"[Theme Edit] Saved gradient edit '{entry.ResourceKey}': " +
                       $"{entry.OriginalStartColor}→{entry.OriginalEndColor} ({entry.OriginalAngle}°) to " +
                       $"{gradient.StartColor}→{gradient.EndColor} ({gradient.Angle}°)", 
                       LogLevel.Info, categoryOverride: "theme");
            
            // Apply the changes immediately so user can see them
            try
            {
                var tempTheme = BuildThemeDefinition(preserveId: true);
                AppServices.ThemeEngine.ApplyThemePreview(tempTheme);
                Logger.Log($"[Theme Edit] Applied gradient changes for live preview", LogLevel.Info, categoryOverride: "theme");
            }
            catch (Exception ex)
            {
                Logger.Log($"[Theme Edit] Failed to apply gradient preview: {ex.Message}", LogLevel.Warning, categoryOverride: "theme");
            }
        }

        entry.IsEditing = false;
        _currentlyEditingGradient = null;
        OnPropertyChanged(nameof(IsEditingGradient));
        
        // Trigger command re-evaluation for IsEditingGradient-dependent commands
        (SaveGradientEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CancelGradientEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (EditGradientCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (EditColorCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (EditMetadataCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (NewFromBlankTemplateCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SaveAsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ExportModifiedThemeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        
        await Task.CompletedTask;
    }

    private void CancelGradientEdit()
    {
        if (_currentlyEditingGradient == null || _currentlyEditingGradient.GradientDefinition == null) return;

        var entry = _currentlyEditingGradient;
        var gradient = entry.GradientDefinition;
        
        gradient.StartColor = entry.OriginalStartColor ?? gradient.StartColor;
        gradient.EndColor = entry.OriginalEndColor ?? gradient.EndColor;
        gradient.Angle = entry.OriginalAngle;
        
        entry.IsEditing = false;
        _currentlyEditingGradient = null;
        OnPropertyChanged(nameof(IsEditingGradient));
        
        // Trigger command re-evaluation for IsEditingGradient-dependent commands
        (SaveGradientEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CancelGradientEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (EditGradientCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (EditColorCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (EditMetadataCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (NewFromBlankTemplateCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SaveAsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ExportModifiedThemeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        
        Logger.Log($"[Theme Edit] Cancelled editing gradient '{entry.ResourceKey}'", LogLevel.Info, categoryOverride: "theme");
    }

    private void ApplyGradientPreset(GradientPreset? preset)
    {
        if (preset == null || _currentlyEditingGradient?.GradientDefinition == null) return;

        // Update editing properties which will automatically update the GradientDefinition
        EditingGradientStartColor = preset.StartColor;
        EditingGradientEndColor = preset.EndColor;
        EditingGradientAngle = preset.Angle;
        
        Logger.Log($"[Theme Edit] Applied gradient preset '{preset.Name}' to '{_currentlyEditingGradient.ResourceKey}'", LogLevel.Info, categoryOverride: "theme");
        
        // Reset selection after a short delay to allow the same preset to be selected again
        // Use dispatcher to avoid triggering the setter during the current setter execution
        Task.Run(async () =>
        {
            await Task.Delay(100);
            _selectedGradientPreset = null;
            OnPropertyChanged(nameof(SelectedGradientPreset));
        });
    }

    private void ClearGradient()
    {
        if (_currentlyEditingGradient == null) return;

        // Find the Main Background color from the theme (App.Background)
        var backgroundEntry = ThemeColors.FirstOrDefault(c => c.ResourceKey == "App.Background");
        var backgroundColor = backgroundEntry?.ColorValue ?? "#1C1C1C";

        // Set both colors to the Main Background color to create a "solid" gradient
        EditingGradientStartColor = backgroundColor;
        EditingGradientEndColor = backgroundColor;
        
        Logger.Log($"[Theme Edit] Cleared gradient '{_currentlyEditingGradient.ResourceKey}' to Main Background color {backgroundColor}", LogLevel.Info, categoryOverride: "theme");
    }

    public async void OpenGradientStartColorPicker()
    {
        if (_currentlyEditingGradient == null) return;

        try
        {
            var dialog = new Zer0Talk.Controls.ColorPicker.ColorPickerDialog();
            // Initialize dialog from the current gradient start color
            InitializeEditingState(EditingGradientStartColor);
            
            // Set initial values from the current color
            dialog.SvPicker.Hue = EditingHue;
            dialog.SvPicker.Saturation = EditingSaturation;
            dialog.SvPicker.Value = EditingValue;
            dialog.HueSlider.Hue = EditingHue;
            dialog.BrightnessSlider.Brightness = EditingValue;
            dialog.RedSlider.Value = (byte)EditingRed;
            dialog.GreenSlider.Value = (byte)EditingGreen;
            dialog.BlueSlider.Value = (byte)EditingBlue;
            dialog.HexInput.Text = EditingHex;

            // Find owner window
            Window? owner = null;
            if (App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                owner = desktop.MainWindow;
            }

            var result = await dialog.ShowDialog<bool?>(owner!);

            // If user applied the selection, update the gradient start color
            if (result == true)
            {
                var (h, s, v) = dialog.GetHsv();
                EditingHue = h;
                EditingSaturation = s;
                EditingValue = v;
                var newColor = ColorFromHsv(EditingHue, EditingSaturation, EditingValue, (byte)EditingAlpha);
                EditingGradientStartColor = ColorToHex(newColor);
            }

            Logger.Log($"[Theme Edit] Gradient start color picker closed (applied: {result})", LogLevel.Info, categoryOverride: "theme");
        }
        catch (Exception ex)
        {
            Logger.Log($"[Theme Edit] Failed to open gradient start color picker: {ex.Message}", LogLevel.Error, categoryOverride: "theme");
        }
    }

    public async void OpenGradientEndColorPicker()
    {
        if (_currentlyEditingGradient == null) return;

        try
        {
            var dialog = new Zer0Talk.Controls.ColorPicker.ColorPickerDialog();
            // Initialize dialog from the current gradient end color
            InitializeEditingState(EditingGradientEndColor);
            
            // Set initial values from the current color
            dialog.SvPicker.Hue = EditingHue;
            dialog.SvPicker.Saturation = EditingSaturation;
            dialog.SvPicker.Value = EditingValue;
            dialog.HueSlider.Hue = EditingHue;
            dialog.BrightnessSlider.Brightness = EditingValue;
            dialog.RedSlider.Value = (byte)EditingRed;
            dialog.GreenSlider.Value = (byte)EditingGreen;
            dialog.BlueSlider.Value = (byte)EditingBlue;
            dialog.HexInput.Text = EditingHex;

            // Find owner window
            Window? owner = null;
            if (App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                owner = desktop.MainWindow;
            }

            var result = await dialog.ShowDialog<bool?>(owner!);

            // If user applied the selection, update the gradient end color
            if (result == true)
            {
                var (h, s, v) = dialog.GetHsv();
                EditingHue = h;
                EditingSaturation = s;
                EditingValue = v;
                var newColor = ColorFromHsv(EditingHue, EditingSaturation, EditingValue, (byte)EditingAlpha);
                EditingGradientEndColor = ColorToHex(newColor);
            }

            Logger.Log($"[Theme Edit] Gradient end color picker closed (applied: {result})", LogLevel.Info, categoryOverride: "theme");
        }
        catch (Exception ex)
        {
            Logger.Log($"[Theme Edit] Failed to open gradient end color picker: {ex.Message}", LogLevel.Error, categoryOverride: "theme");
        }
    }

    #endregion

    #region Metadata Editing Methods

    private void StartEditingMetadata()
    {
        IsEditingMetadata = true;
        EditableThemeName = CurrentThemeDisplayName;
        EditableThemeDescription = CurrentThemeDescription;
        EditableThemeAuthor = CurrentThemeAuthor;
        EditableThemeVersion = CurrentThemeVersion;
        
        Logger.Log("[Theme Edit] Started editing metadata", LogLevel.Info, categoryOverride: "theme");
    }

    private async Task SaveMetadataAsync()
    {
        CurrentThemeDisplayName = EditableThemeName;
        CurrentThemeDescription = EditableThemeDescription;
        CurrentThemeAuthor = EditableThemeAuthor;
        CurrentThemeVersion = EditableThemeVersion;
        
        IsEditingMetadata = false;
        Logger.Log("[Theme Edit] Saved metadata changes", LogLevel.Info, categoryOverride: "theme");
        
        await Task.CompletedTask;
    }

    private void CancelMetadataEdit()
    {
        IsEditingMetadata = false;
        Logger.Log("[Theme Edit] Cancelled metadata editing", LogLevel.Info, categoryOverride: "theme");
    }

    #endregion

    #region Theme Loading Methods

    public async Task NewFromBlankTemplateAsync()
    {
        try
        {
            var blank = ThemeDefinition.CreateBlankTemplate();
            
            // Clear file path since this is a new theme
            _currentThemeFilePath = null;
            _currentThemeCreatedAt = DateTime.UtcNow;
            
            Logger.Log("[Blank Template] Loading blank template into editor", LogLevel.Info, categoryOverride: "theme");

            CurrentThemeId = blank.Id;
            CurrentThemeDisplayName = blank.DisplayName;
            CurrentThemeDescription = blank.Description ?? "No description available";
            CurrentThemeVersion = blank.Version;
            CurrentThemeAuthor = blank.Author ?? "Unknown";
            CurrentThemeIsReadOnly = blank.IsReadOnly;
            CurrentThemeBaseVariant = blank.BaseVariant ?? "Dark";
            _currentThemeResourceDictionaries = blank.ResourceDictionaries != null
                ? new System.Collections.Generic.List<string>(blank.ResourceDictionaries)
                : new System.Collections.Generic.List<string>();
            OnPropertyChanged(nameof(CurrentThemeResourceDictionaries));
            CurrentThemeDefaultFontFamily = blank.DefaultFontFamily;
            CurrentThemeDefaultUiScale = blank.DefaultUiScale <= 0 ? 1.0 : blank.DefaultUiScale;
            _currentThemeTags = blank.Tags != null
                ? new System.Collections.Generic.List<string>(blank.Tags)
                : new System.Collections.Generic.List<string>();
            OnPropertyChanged(nameof(CurrentThemeTags));
            _currentThemeMetadata = blank.Metadata != null
                ? new System.Collections.Generic.Dictionary<string, string>(blank.Metadata)
                : new System.Collections.Generic.Dictionary<string, string>();
            OnPropertyChanged(nameof(CurrentThemeMetadata));
            _currentThemeMinAppVersion = blank.MinAppVersion;
            _currentThemeAllowsCustomization = blank.AllowsCustomization;
            _currentThemeIsLegacyTheme = blank.IsLegacyTheme;
            _currentThemeLegacyOption = blank.LegacyThemeOption;

            _undoStack.Clear();
            _redoStack.Clear();
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));

            ThemeColors.Clear();
            if (blank.ColorOverrides != null)
            {
                foreach (var kvp in blank.ColorOverrides.OrderBy(x => x.Key))
                {
                    ThemeColors.Add(new ThemeColorEntry
                    {
                        ResourceKey = kvp.Key,
                        ColorValue = kvp.Value,
                        IsEditable = true
                    });
                }
            }

            ThemeGradients.Clear();
            if (blank.Gradients != null)
            {
                foreach (var kvp in blank.Gradients.OrderBy(x => x.Key))
                {
                    ThemeGradients.Add(new ThemeGradientEntry
                    {
                        ResourceKey = kvp.Key,
                        DisplayName = GetFriendlyGradientName(kvp.Key),
                        GradientDefinition = kvp.Value,
                        IsEditable = true
                    });
                }
            }

            EnsureCatalogCoverage();

            Logger.Log("[Blank Template] Successfully loaded blank template", LogLevel.Info, categoryOverride: "theme");
        }
        catch (Exception ex)
        {
            Logger.Log($"[Blank Template] Error loading blank template: {ex.Message}", LogLevel.Error, categoryOverride: "theme");
        }
        
        await Task.CompletedTask;
    }



    public async Task ImportThemeAsync()
    {
        try
        {
            var window = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (window == null)
            {
                Logger.Log("[Theme Import] Cannot open file dialog - no window", LogLevel.Error, categoryOverride: "theme");
                return;
            }

            var files = await window.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Import Theme",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Zer0Talk Theme Files")
                    {
                        Patterns = new[] { "*.zttheme" }
                    },
                    new Avalonia.Platform.Storage.FilePickerFileType("All Files")
                    {
                        Patterns = new[] { "*" }
                    }
                }
            });

            if (files.Count == 0) return;

            var filePath = files[0].Path.LocalPath;
            var fileName = System.IO.Path.GetFileName(filePath);

            var themeDef = ThemeDefinition.LoadFromFile(filePath, out var warnings);

            // Track the loaded file path and creation time
            _currentThemeFilePath = filePath;
            _currentThemeCreatedAt = themeDef.CreatedAt;

            CurrentThemeId = themeDef.Id;
            CurrentThemeDisplayName = themeDef.DisplayName;
            CurrentThemeDescription = themeDef.Description ?? "No description available";
            CurrentThemeVersion = themeDef.Version;
            CurrentThemeAuthor = themeDef.Author ?? "Unknown";
            CurrentThemeIsReadOnly = false;
            CurrentThemeBaseVariant = themeDef.BaseVariant ?? "Dark";
            _currentThemeResourceDictionaries = themeDef.ResourceDictionaries != null
                ? new System.Collections.Generic.List<string>(themeDef.ResourceDictionaries)
                : new System.Collections.Generic.List<string>();
            OnPropertyChanged(nameof(CurrentThemeResourceDictionaries));
            CurrentThemeDefaultFontFamily = themeDef.DefaultFontFamily;
            CurrentThemeDefaultUiScale = themeDef.DefaultUiScale <= 0 ? 1.0 : themeDef.DefaultUiScale;
            _currentThemeTags = themeDef.Tags != null
                ? new System.Collections.Generic.List<string>(themeDef.Tags)
                : new System.Collections.Generic.List<string>();
            OnPropertyChanged(nameof(CurrentThemeTags));
            _currentThemeMetadata = themeDef.Metadata != null
                ? new System.Collections.Generic.Dictionary<string, string>(themeDef.Metadata)
                : new System.Collections.Generic.Dictionary<string, string>();
            OnPropertyChanged(nameof(CurrentThemeMetadata));
            _currentThemeMinAppVersion = themeDef.MinAppVersion;
            _currentThemeAllowsCustomization = themeDef.AllowsCustomization;
            _currentThemeIsLegacyTheme = themeDef.IsLegacyTheme;
            _currentThemeLegacyOption = themeDef.LegacyThemeOption;

            ThemeColors.Clear();
            if (themeDef.ColorOverrides != null)
            {
                foreach (var kvp in themeDef.ColorOverrides.OrderBy(x => x.Key))
                {
                    ThemeColors.Add(new ThemeColorEntry
                    {
                        ResourceKey = kvp.Key,
                        ColorValue = kvp.Value,
                        IsEditable = true
                    });
                }
            }

            ThemeGradients.Clear();
            if (themeDef.Gradients != null)
            {
                foreach (var kvp in themeDef.Gradients.OrderBy(x => x.Key))
                {
                    // Ensure gradient has valid colors, otherwise use defaults
                    var gradient = kvp.Value;
                    if (string.IsNullOrWhiteSpace(gradient.StartColor))
                    {
                        gradient.StartColor = "#3A3A3A";
                        Logger.Log($"[Theme Import] Gradient '{kvp.Key}' had empty StartColor, set to default", LogLevel.Warning, categoryOverride: "theme");
                    }
                    if (string.IsNullOrWhiteSpace(gradient.EndColor))
                    {
                        gradient.EndColor = "#1C1C1C";
                        Logger.Log($"[Theme Import] Gradient '{kvp.Key}' had empty EndColor, set to default", LogLevel.Warning, categoryOverride: "theme");
                    }
                    
                    ThemeGradients.Add(new ThemeGradientEntry
                    {
                        ResourceKey = kvp.Key,
                        DisplayName = GetFriendlyGradientName(kvp.Key),
                        GradientDefinition = gradient,
                        IsEditable = true
                    });
                }
            }

            EnsureCatalogCoverage();

            // Populate editable metadata fields so they're visible immediately
            EditableThemeName = CurrentThemeDisplayName;
            EditableThemeDescription = CurrentThemeDescription;
            EditableThemeAuthor = CurrentThemeAuthor;
            EditableThemeVersion = CurrentThemeVersion;

            // Raise CanExecuteChanged for Save command since we now have a file path
            (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();

            Logger.Log($"[Theme Import] Successfully imported theme '{themeDef.DisplayName}' from {fileName}", LogLevel.Info, categoryOverride: "theme");
        }
        catch (Exception ex)
        {
            Logger.Log($"[Theme Import] Error importing theme: {ex.Message}", LogLevel.Error, categoryOverride: "theme");
        }
    }

    #endregion

    #region Theme Saving Methods

    private ThemeDefinition BuildThemeDefinition(bool preserveId)
    {
        var theme = new ThemeDefinition
        {
            Id = preserveId && !string.IsNullOrWhiteSpace(CurrentThemeId) 
                ? CurrentThemeId 
                : $"custom-{Guid.NewGuid():N}",
            DisplayName = string.IsNullOrWhiteSpace(CurrentThemeDisplayName) ? "Untitled Theme" : CurrentThemeDisplayName,
            Description = CurrentThemeDescription,
            Author = CurrentThemeAuthor,
            Version = string.IsNullOrWhiteSpace(CurrentThemeVersion) ? "1.0.0" : CurrentThemeVersion,
            CreatedAt = preserveId ? _currentThemeCreatedAt : DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            IsReadOnly = false,
            AllowsCustomization = true,
            ThemeType = ThemeType.Custom,
            BaseVariant = string.IsNullOrWhiteSpace(CurrentThemeBaseVariant) ? "Dark" : CurrentThemeBaseVariant,
            ResourceDictionaries = new System.Collections.Generic.List<string>(_currentThemeResourceDictionaries),
            DefaultFontFamily = CurrentThemeDefaultFontFamily,
            DefaultUiScale = Math.Clamp(CurrentThemeDefaultUiScale <= 0 ? 1.0 : CurrentThemeDefaultUiScale, 0.5, 3.0),
            MinAppVersion = _currentThemeMinAppVersion,
            Metadata = new System.Collections.Generic.Dictionary<string, string>(_currentThemeMetadata),
            Tags = new System.Collections.Generic.List<string>(_currentThemeTags),
            IsLegacyTheme = false,
            LegacyThemeOption = null,
            ColorOverrides = new System.Collections.Generic.Dictionary<string, string>(),
            Gradients = new System.Collections.Generic.Dictionary<string, GradientDefinition>()
        };

        foreach (var colorEntry in ThemeColors)
        {
            theme.ColorOverrides[colorEntry.ResourceKey] = colorEntry.ColorValue;
        }

        foreach (var gradientEntry in ThemeGradients)
        {
            if (gradientEntry.GradientDefinition != null)
            {
                theme.Gradients[gradientEntry.ResourceKey] = gradientEntry.GradientDefinition;
            }
        }

        if (!theme.Tags.Any(t => string.Equals(t, "custom", StringComparison.OrdinalIgnoreCase)))
        {
            theme.Tags.Add("custom");
        }

        return theme;
    }

    public async Task SaveAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_currentThemeFilePath))
            {
                Logger.Log("[Theme Save] No file path set, falling back to Save As", LogLevel.Warning, categoryOverride: "theme");
                await SaveAsAsync();
                return;
            }

            // Build theme with preserved ID and CreatedAt
            var theme = BuildThemeDefinition(preserveId: true);
            
            // Save to existing file
            theme.SaveToFile(_currentThemeFilePath);

            Logger.Log($"[Theme Save] Successfully saved theme to: {_currentThemeFilePath}", LogLevel.Info, categoryOverride: "theme");

            // Reload themes to update the registration
            var themesDir = AppServices.ThemeEngine.GetCustomThemesDirectory();
            if (_currentThemeFilePath.StartsWith(themesDir, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var loaded = AppServices.ThemeEngine.LoadCustomThemes();
                    Logger.Log($"[Theme Save] Reloaded {loaded} custom themes", LogLevel.Info, categoryOverride: "theme");
                }
                catch (Exception ex)
                {
                    Logger.Log($"[Theme Save] Failed to reload custom themes: {ex.Message}", LogLevel.Warning, categoryOverride: "theme");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[Theme Save] Error saving theme: {ex.Message}", LogLevel.Error, categoryOverride: "theme");
        }
    }

    public async Task SaveAsAsync()
    {
        try
        {
            // Build theme with new ID for SaveAs
            var theme = BuildThemeDefinition(preserveId: false);

            var window = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (window == null)
            {
                Logger.Log("[Theme SaveAs] Cannot open file dialog - no window", LogLevel.Error, categoryOverride: "theme");
                return;
            }

            // Get custom themes directory as default location
            var themesDir = AppServices.ThemeEngine.GetCustomThemesDirectory();
            var defaultPath = System.IO.Path.Combine(themesDir, $"{theme.DisplayName}.zttheme");

            var file = await window.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Save Theme As",
                SuggestedFileName = $"{theme.DisplayName}.zttheme",
                SuggestedStartLocation = await window.StorageProvider.TryGetFolderFromPathAsync(new Uri(themesDir)),
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Zer0Talk Theme Files")
                    {
                        Patterns = new[] { "*.zttheme" }
                    },
                    new Avalonia.Platform.Storage.FilePickerFileType("All Files")
                    {
                        Patterns = new[] { "*" }
                    }
                }
            });

            if (file == null)
            {
                Logger.Log("[Theme SaveAs] User cancelled file dialog", LogLevel.Info, categoryOverride: "theme");
                return;
            }

            var filePath = file.Path.LocalPath;
            theme.SaveToFile(filePath);

            // Update tracking variables for the newly saved theme
            _currentThemeFilePath = filePath;
            _currentThemeCreatedAt = theme.CreatedAt;
            CurrentThemeId = theme.Id;
            
            // Notify that Save command can now execute
            (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();

            Logger.Log($"[Theme SaveAs] Successfully saved theme to: {filePath}", LogLevel.Info, categoryOverride: "theme");

            // If saved to custom themes directory, reload themes and register it
            if (filePath.StartsWith(themesDir, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var loaded = AppServices.ThemeEngine.LoadCustomThemes();
                    Logger.Log($"[Theme SaveAs] Reloaded {loaded} custom themes including newly saved theme", LogLevel.Info, categoryOverride: "theme");
                }
                catch (Exception ex)
                {
                    Logger.Log($"[Theme SaveAs] Failed to reload custom themes: {ex.Message}", LogLevel.Warning, categoryOverride: "theme");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[Theme SaveAs] Error saving theme: {ex.Message}", LogLevel.Error, categoryOverride: "theme");
        }
    }

    public async Task ExportModifiedThemeAsync()
    {
        await SaveAsAsync();
    }

    public async Task SearchDrivesForThemesAsync()
    {
        try
        {
            Logger.Log("[Theme Search] Starting drive search for themes...", LogLevel.Info, categoryOverride: "theme");

            var cts = new System.Threading.CancellationTokenSource();
            var foundThemes = new System.Collections.Generic.List<string>();

            // Show progress in a simple way (could be enhanced with a dialog)
            var progressHandler = new Action<string>(msg => 
            {
                Logger.Log($"[Theme Search] {msg}", LogLevel.Info, categoryOverride: "theme");
            });

            // Search drives
            foundThemes = await AppServices.ThemeEngine.SearchDrivesForThemesAsync(progressHandler, cts.Token);

            if (foundThemes.Count == 0)
            {
                Logger.Log("[Theme Search] No theme files found", LogLevel.Info, categoryOverride: "theme");
                return;
            }

            Logger.Log($"[Theme Search] Found {foundThemes.Count} theme files. Loading...", LogLevel.Info, categoryOverride: "theme");

            // Load found themes
            var loaded = AppServices.ThemeEngine.LoadThemesFromPaths(foundThemes);
            
            Logger.Log($"[Theme Search] Successfully loaded {loaded} themes from search results", LogLevel.Info, categoryOverride: "theme");
        }
        catch (Exception ex)
        {
            Logger.Log($"[Theme Search] Error during drive search: {ex.Message}", LogLevel.Error, categoryOverride: "theme");
        }
    }

    #endregion

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion

    #region Helper Methods

    private static string GetFriendlyGradientName(string resourceKey)
    {
        return resourceKey switch
        {
            // Window & Title Bar
            "App.TitleBarBackground" => "Title Bar / Drag Bar Background",
            "App.WindowTitleBar" => "Window Title Bar",
            "App.DragBarBackground" => "Drag Bar Background",
            
            // Backgrounds
            "App.BackgroundGradient" => "Main Background Gradient",
            "App.SidebarGradient" => "Sidebar Background Gradient",
            "App.PanelGradient" => "Panel Background Gradient",
            
            // Messages & Chat
            "App.MessageBubbleGradient" => "Message Bubble Gradient",
            "App.ChatBackgroundGradient" => "Chat Area Background",
            "App.SentMessageGradient" => "Sent Message Bubble",
            "App.ReceivedMessageGradient" => "Received Message Bubble",
            
            // Buttons & Controls
            "App.ButtonGradient" => "Button Background Gradient",
            "App.ButtonHoverGradient" => "Button Hover Gradient",
            "App.AccentGradient" => "Accent Gradient",
            
            // Headers & Sections
            "App.HeaderGradient" => "Header Background Gradient",
            "App.SectionHeaderGradient" => "Section Header",
            
            // Fallback: Clean up the resource key
            _ => resourceKey.Replace("App.", "").Replace(".", " ")
        };
    }

    private void EnsureCatalogCoverage()
    {
        var template = ThemeDefinition.CreateBlankTemplate();

        if (template.ColorOverrides != null)
        {
            var existingColorKeys = ThemeColors
                .Select(c => c.ResourceKey)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var kvp in template.ColorOverrides)
            {
                if (existingColorKeys.Contains(kvp.Key))
                    continue;

                ThemeColors.Add(new ThemeColorEntry
                {
                    ResourceKey = kvp.Key,
                    ColorValue = kvp.Value,
                    IsEditable = true
                });

                existingColorKeys.Add(kvp.Key);
            }
        }

        if (template.Gradients != null)
        {
            var existingGradientKeys = ThemeGradients
                .Select(g => g.ResourceKey)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var kvp in template.Gradients)
            {
                if (existingGradientKeys.Contains(kvp.Key))
                    continue;

                ThemeGradients.Add(new ThemeGradientEntry
                {
                    ResourceKey = kvp.Key,
                    DisplayName = GetFriendlyGradientName(kvp.Key),
                    GradientDefinition = kvp.Value,
                    IsEditable = true
                });

                existingGradientKeys.Add(kvp.Key);
            }
        }

        SortEditorCollections();
    }

    private void SortEditorCollections()
    {
        var sortedColors = ThemeColors
            .OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.ResourceKey, StringComparer.Ordinal)
            .ToList();

        if (sortedColors.Count != ThemeColors.Count)
        {
            ThemeColors.Clear();
            foreach (var entry in sortedColors)
            {
                ThemeColors.Add(entry);
            }
        }
        else
        {
            for (var i = 0; i < sortedColors.Count; i++)
            {
                if (!ReferenceEquals(sortedColors[i], ThemeColors[i]))
                {
                    ThemeColors.Clear();
                    foreach (var entry in sortedColors)
                    {
                        ThemeColors.Add(entry);
                    }
                    break;
                }
            }
        }

        var sortedGradients = ThemeGradients
            .OrderBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.ResourceKey, StringComparer.Ordinal)
            .ToList();

        if (sortedGradients.Count != ThemeGradients.Count)
        {
            ThemeGradients.Clear();
            foreach (var entry in sortedGradients)
            {
                ThemeGradients.Add(entry);
            }
        }
        else
        {
            for (var i = 0; i < sortedGradients.Count; i++)
            {
                if (!ReferenceEquals(sortedGradients[i], ThemeGradients[i]))
                {
                    ThemeGradients.Clear();
                    foreach (var entry in sortedGradients)
                    {
                        ThemeGradients.Add(entry);
                    }
                    break;
                }
            }
        }
    }

    #endregion

    #region Helper Classes

    private class ColorEditAction
    {
        public string ResourceKey { get; set; } = string.Empty;
        public string OldValue { get; set; } = string.Empty;
        public string NewValue { get; set; } = string.Empty;
    }

    public class ThemeColorEntry : INotifyPropertyChanged
    {
        private string _colorValue = string.Empty;
        private bool _isEditing;
        private bool _isSelected;
        private Color _previewColor = Color.FromArgb(0, 0, 0, 0);
        
        public string ResourceKey { get; set; } = string.Empty;
        public string DisplayName => GetFriendlyName(ResourceKey);
        
        public string ColorValue
        {
            get => _colorValue;
            set
            {
                if (_colorValue != value)
                {
                    _colorValue = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ColorValue)));
                    UpdatePreviewColor();
                }
            }
        }
        
        public Color PreviewColor
        {
            get => _previewColor;
            private set
            {
                if (_previewColor != value)
                {
                    _previewColor = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PreviewColor)));
                }
            }
        }
        
        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (_isEditing != value)
                {
                    _isEditing = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEditing)));
                }
            }
        }
        
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }
        
        public string? OriginalValue { get; set; }
        public bool IsEditable { get; set; } = true;

        private void UpdatePreviewColor()
        {
            if (TryParseColor(_colorValue, out var color))
            {
                PreviewColor = color;
            }
            else
            {
                PreviewColor = Color.FromArgb(0, 0, 0, 0);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        
        private static string GetFriendlyName(string resourceKey)
        {
            return resourceKey switch
            {
                "App.Accent" => "Accent Color (Primary Highlight)",
                "App.AccentLight" => "Accent Light (Hover Effects)",
                "App.AccentDark" => "Accent Dark (Active States)",
                "App.Background" => "Main Background",
                "App.Border" => "Border Color",
                "App.ButtonBackground" => "Button Background",
                "App.ButtonBackgroundHover" => "Button Hover Background",
                "App.ButtonBackgroundPressed" => "Button Pressed Background",
                "App.ButtonForeground" => "Button Text Color",
                "App.ButtonHover" => "Button Hover Background",
                "App.ButtonPressed" => "Button Pressed Background",
                "App.CardBackground" => "Card/Panel Background",
                "App.Danger" => "Danger/Error Color",
                "App.DangerHover" => "Danger Hover",
                "App.ForegroundPrimary" => "Primary Text Color",
                "App.ForegroundSecondary" => "Secondary Text Color",
                "App.TitleBarForeground" => "Title Bar Text",
                "App.Success" => "Success/Confirmation Color",
                "App.SuccessHover" => "Success Hover",
                "App.Warning" => "Warning/Caution Color",
                "App.WarningHover" => "Warning Hover",
                "App.Surface" => "Surface Background",
                "App.SurfaceVariant" => "Surface Variant",
                "App.OnSurface" => "Text on Surface",
                "App.OnSurfaceVariant" => "Text on Surface Variant",
                "App.Outline" => "Outline Color",
                "App.OutlineVariant" => "Outline Variant",
                "App.Shadow" => "Shadow Color",
                "App.Error" => "Error State",
                "App.ErrorContainer" => "Error Background Container",
                "App.OnError" => "Text on Error",
                "App.OnErrorContainer" => "Text on Error Container",
                "App.Primary" => "Primary Color",
                "App.PrimaryContainer" => "Primary Background Container",
                "App.OnPrimary" => "Text on Primary",
                "App.OnPrimaryContainer" => "Text on Primary Container",
                "App.Secondary" => "Secondary Color",
                "App.SecondaryContainer" => "Secondary Background Container",
                "App.OnSecondary" => "Text on Secondary",
                "App.OnSecondaryContainer" => "Text on Secondary Container",
                "App.Tertiary" => "Tertiary Color",
                "App.TertiaryContainer" => "Tertiary Background Container",
                "App.OnTertiary" => "Text on Tertiary",
                "App.OnTertiaryContainer" => "Text on Tertiary Container",
                "App.InverseSurface" => "Inverse Surface",
                "App.InverseOnSurface" => "Text on Inverse Surface",
                "App.InversePrimary" => "Inverse Primary",
                "App.Scrim" => "Scrim/Overlay Color",
                "App.SurfaceTint" => "Surface Tint",
                _ => resourceKey.Replace("App.", "").Replace(".", " ") // Fallback: remove prefix and dots
            };
        }
    }

    public class ThemeGradientEntry : INotifyPropertyChanged
    {
        private GradientDefinition? _gradientDefinition;
        private bool _isEditing = false;
        
        public string ResourceKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        
        public GradientDefinition? GradientDefinition
        {
            get => _gradientDefinition;
            set
            {
                if (_gradientDefinition != value)
                {
                    _gradientDefinition = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GradientDefinition)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GradientPreview)));
                }
            }
        }
        
        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (_isEditing != value)
                {
                    _isEditing = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEditing)));
                }
            }
        }
        
        public bool IsEditable { get; set; } = true;
        
        public string? OriginalStartColor { get; set; }
        public string? OriginalEndColor { get; set; }
        public double OriginalAngle { get; set; }
        
        public string GradientPreview => GradientDefinition != null 
            ? $"{GradientDefinition.StartColor} → {GradientDefinition.EndColor} ({GradientDefinition.Angle}°)"
            : "No gradient data";

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public class GradientPreset
    {
        public string Name { get; set; } = string.Empty;
        public string StartColor { get; set; } = "#000000";
        public string EndColor { get; set; } = "#FFFFFF";
        public double Angle { get; set; } = 0.0;
    }

    #endregion
}
