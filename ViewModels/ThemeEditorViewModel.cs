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

    // System accent color overrides - these prevent OS colors from showing through
    private string? _systemAccentColor;
    public string? SystemAccentColor
    {
        get => _systemAccentColor;
        set
        {
            if (_systemAccentColor != value)
            {
                _systemAccentColor = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SystemAccentColorPreview));
            }
        }
    }

    private string? _systemAccentColor2;
    public string? SystemAccentColor2
    {
        get => _systemAccentColor2;
        set
        {
            if (_systemAccentColor2 != value)
            {
                _systemAccentColor2 = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SystemAccentColor2Preview));
            }
        }
    }

    private string? _systemAccentColor3;
    public string? SystemAccentColor3
    {
        get => _systemAccentColor3;
        set
        {
            if (_systemAccentColor3 != value)
            {
                _systemAccentColor3 = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SystemAccentColor3Preview));
            }
        }
    }

    private string? _systemAccentColor4;
    public string? SystemAccentColor4
    {
        get => _systemAccentColor4;
        set
        {
            if (_systemAccentColor4 != value)
            {
                _systemAccentColor4 = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SystemAccentColor4Preview));
            }
        }
    }

    private string? _systemAccentColorLight;
    public string? SystemAccentColorLight
    {
        get => _systemAccentColorLight;
        set
        {
            if (_systemAccentColorLight != value)
            {
                _systemAccentColorLight = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SystemAccentColorLightPreview));
            }
        }
    }

    private string? _systemListLowColor;
    public string? SystemListLowColor
    {
        get => _systemListLowColor;
        set
        {
            if (_systemListLowColor != value)
            {
                _systemListLowColor = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SystemListLowColorPreview));
            }
        }
    }

    private string? _systemListMediumColor;
    public string? SystemListMediumColor
    {
        get => _systemListMediumColor;
        set
        {
            if (_systemListMediumColor != value)
            {
                _systemListMediumColor = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SystemListMediumColorPreview));
            }
        }
    }

    // SystemAlt color properties (layering/depth)
    private string? _systemAltHighColor;
    public string? SystemAltHighColor
    {
        get => _systemAltHighColor;
        set
        {
            if (_systemAltHighColor != value)
            {
                _systemAltHighColor = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SystemAltHighColorPreview));
            }
        }
    }

    private string? _systemAltMediumHighColor;
    public string? SystemAltMediumHighColor
    {
        get => _systemAltMediumHighColor;
        set
        {
            if (_systemAltMediumHighColor != value)
            {
                _systemAltMediumHighColor = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SystemAltMediumHighColorPreview));
            }
        }
    }

    private string? _systemAltMediumColor;
    public string? SystemAltMediumColor
    {
        get => _systemAltMediumColor;
        set
        {
            if (_systemAltMediumColor != value)
            {
                _systemAltMediumColor = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SystemAltMediumColorPreview));
            }
        }
    }

    private string? _systemAltMediumLowColor;
    public string? SystemAltMediumLowColor
    {
        get => _systemAltMediumLowColor;
        set
        {
            if (_systemAltMediumLowColor != value)
            {
                _systemAltMediumLowColor = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SystemAltMediumLowColorPreview));
            }
        }
    }

    private string? _systemAltLowColor;
    public string? SystemAltLowColor
    {
        get => _systemAltLowColor;
        set
        {
            if (_systemAltLowColor != value)
            {
                _systemAltLowColor = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SystemAltLowColorPreview));
            }
        }
    }

    // SystemBase color properties (foundation layers)
    private string? _systemBaseHighColor;
    public string? SystemBaseHighColor
    {
        get => _systemBaseHighColor;
        set
        {
            if (_systemBaseHighColor != value)
            {
                _systemBaseHighColor = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SystemBaseHighColorPreview));
            }
        }
    }

    private string? _systemBaseMediumHighColor;
    public string? SystemBaseMediumHighColor
    {
        get => _systemBaseMediumHighColor;
        set
        {
            if (_systemBaseMediumHighColor != value)
            {
                _systemBaseMediumHighColor = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SystemBaseMediumHighColorPreview));
            }
        }
    }

    private string? _systemBaseMediumColor;
    public string? SystemBaseMediumColor
    {
        get => _systemBaseMediumColor;
        set
        {
            if (_systemBaseMediumColor != value)
            {
                _systemBaseMediumColor = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SystemBaseMediumColorPreview));
            }
        }
    }

    private string? _systemBaseMediumLowColor;
    public string? SystemBaseMediumLowColor
    {
        get => _systemBaseMediumLowColor;
        set
        {
            if (_systemBaseMediumLowColor != value)
            {
                _systemBaseMediumLowColor = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SystemBaseMediumLowColorPreview));
            }
        }
    }

    private string? _systemBaseLowColor;
    public string? SystemBaseLowColor
    {
        get => _systemBaseLowColor;
        set
        {
            if (_systemBaseLowColor != value)
            {
                _systemBaseLowColor = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SystemBaseLowColorPreview));
            }
        }
    }

    // SystemChrome color properties (window/frame colors)
    private string? _systemChromeAltLowColor;
    public string? SystemChromeAltLowColor { get => _systemChromeAltLowColor; set { if (_systemChromeAltLowColor != value) { _systemChromeAltLowColor = value; OnPropertyChanged(); OnPropertyChanged(nameof(SystemChromeAltLowColorPreview)); } } }

    private string? _systemChromeBlackHighColor;
    public string? SystemChromeBlackHighColor { get => _systemChromeBlackHighColor; set { if (_systemChromeBlackHighColor != value) { _systemChromeBlackHighColor = value; OnPropertyChanged(); OnPropertyChanged(nameof(SystemChromeBlackHighColorPreview)); } } }

    private string? _systemChromeBlackLowColor;
    public string? SystemChromeBlackLowColor { get => _systemChromeBlackLowColor; set { if (_systemChromeBlackLowColor != value) { _systemChromeBlackLowColor = value; OnPropertyChanged(); OnPropertyChanged(nameof(SystemChromeBlackLowColorPreview)); } } }

    private string? _systemChromeBlackMediumColor;
    public string? SystemChromeBlackMediumColor { get => _systemChromeBlackMediumColor; set { if (_systemChromeBlackMediumColor != value) { _systemChromeBlackMediumColor = value; OnPropertyChanged(); OnPropertyChanged(nameof(SystemChromeBlackMediumColorPreview)); } } }

    private string? _systemChromeBlackMediumLowColor;
    public string? SystemChromeBlackMediumLowColor { get => _systemChromeBlackMediumLowColor; set { if (_systemChromeBlackMediumLowColor != value) { _systemChromeBlackMediumLowColor = value; OnPropertyChanged(); OnPropertyChanged(nameof(SystemChromeBlackMediumLowColorPreview)); } } }

    private string? _systemChromeDisabledHighColor;
    public string? SystemChromeDisabledHighColor { get => _systemChromeDisabledHighColor; set { if (_systemChromeDisabledHighColor != value) { _systemChromeDisabledHighColor = value; OnPropertyChanged(); OnPropertyChanged(nameof(SystemChromeDisabledHighColorPreview)); } } }

    private string? _systemChromeDisabledLowColor;
    public string? SystemChromeDisabledLowColor { get => _systemChromeDisabledLowColor; set { if (_systemChromeDisabledLowColor != value) { _systemChromeDisabledLowColor = value; OnPropertyChanged(); OnPropertyChanged(nameof(SystemChromeDisabledLowColorPreview)); } } }

    private string? _systemChromeGrayColor;
    public string? SystemChromeGrayColor { get => _systemChromeGrayColor; set { if (_systemChromeGrayColor != value) { _systemChromeGrayColor = value; OnPropertyChanged(); OnPropertyChanged(nameof(SystemChromeGrayColorPreview)); } } }

    private string? _systemChromeHighColor;
    public string? SystemChromeHighColor { get => _systemChromeHighColor; set { if (_systemChromeHighColor != value) { _systemChromeHighColor = value; OnPropertyChanged(); OnPropertyChanged(nameof(SystemChromeHighColorPreview)); } } }

    private string? _systemChromeLowColor;
    public string? SystemChromeLowColor { get => _systemChromeLowColor; set { if (_systemChromeLowColor != value) { _systemChromeLowColor = value; OnPropertyChanged(); OnPropertyChanged(nameof(SystemChromeLowColorPreview)); } } }

    private string? _systemChromeMediumColor;
    public string? SystemChromeMediumColor { get => _systemChromeMediumColor; set { if (_systemChromeMediumColor != value) { _systemChromeMediumColor = value; OnPropertyChanged(); OnPropertyChanged(nameof(SystemChromeMediumColorPreview)); } } }

    private string? _systemChromeMediumLowColor;
    public string? SystemChromeMediumLowColor { get => _systemChromeMediumLowColor; set { if (_systemChromeMediumLowColor != value) { _systemChromeMediumLowColor = value; OnPropertyChanged(); OnPropertyChanged(nameof(SystemChromeMediumLowColorPreview)); } } }

    private string? _systemChromeWhiteColor;
    public string? SystemChromeWhiteColor { get => _systemChromeWhiteColor; set { if (_systemChromeWhiteColor != value) { _systemChromeWhiteColor = value; OnPropertyChanged(); OnPropertyChanged(nameof(SystemChromeWhiteColorPreview)); } } }

    // Color preview properties for binding
    public Color SystemAccentColorPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SystemAccentColor) || !TryParseColor(SystemAccentColor, out var color))
                return Color.Parse("#0078D4"); // Windows blue
            return color;
        }
    }

    public Color SystemAccentColor2Preview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SystemAccentColor2) || !TryParseColor(SystemAccentColor2, out var color))
                return Color.Parse("#005A9E"); // Darker blue
            return color;
        }
    }

    public Color SystemAccentColor3Preview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SystemAccentColor3) || !TryParseColor(SystemAccentColor3, out var color))
                return Color.Parse("#106EBE"); // Medium blue
            return color;
        }
    }

    public Color SystemAccentColor4Preview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SystemAccentColor4) || !TryParseColor(SystemAccentColor4, out var color))
                return Color.Parse("#0086F0"); // Lighter blue
            return color;
        }
    }

    public Color SystemAccentColorLightPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SystemAccentColorLight) || !TryParseColor(SystemAccentColorLight, out var color))
                return Color.Parse("#60CDFF"); // Bright blue for hover
            return color;
        }
    }

    public Color SystemListLowColorPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SystemListLowColor) || !TryParseColor(SystemListLowColor, out var color))
                return Color.Parse("#33FFFFFF"); // 20% white - subtle hover
            return color;
        }
    }

    public Color SystemListMediumColorPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SystemListMediumColor) || !TryParseColor(SystemListMediumColor, out var color))
                return Color.Parse("#66FFFFFF"); // 40% white - medium hover
            return color;
        }
    }

    public Color SystemAltHighColorPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SystemAltHighColor) || !TryParseColor(SystemAltHighColor, out var color))
                return Color.Parse("#FFFFFFFF"); // 100% white - high contrast
            return color;
        }
    }

    public Color SystemAltMediumHighColorPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SystemAltMediumHighColor) || !TryParseColor(SystemAltMediumHighColor, out var color))
                return Color.Parse("#CCFFFFFF"); // 80% white
            return color;
        }
    }

    public Color SystemAltMediumColorPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SystemAltMediumColor) || !TryParseColor(SystemAltMediumColor, out var color))
                return Color.Parse("#99FFFFFF"); // 60% white
            return color;
        }
    }

    public Color SystemAltMediumLowColorPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SystemAltMediumLowColor) || !TryParseColor(SystemAltMediumLowColor, out var color))
                return Color.Parse("#66FFFFFF"); // 40% white
            return color;
        }
    }

    public Color SystemAltLowColorPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SystemAltLowColor) || !TryParseColor(SystemAltLowColor, out var color))
                return Color.Parse("#33FFFFFF"); // 20% white
            return color;
        }
    }

    public Color SystemBaseHighColorPreview { get { if (string.IsNullOrWhiteSpace(SystemBaseHighColor) || !TryParseColor(SystemBaseHighColor, out var color)) return Color.Parse("#FFFFFF"); return color; } }
    public Color SystemBaseMediumHighColorPreview { get { if (string.IsNullOrWhiteSpace(SystemBaseMediumHighColor) || !TryParseColor(SystemBaseMediumHighColor, out var color)) return Color.Parse("#CCCCCC"); return color; } }
    public Color SystemBaseMediumColorPreview { get { if (string.IsNullOrWhiteSpace(SystemBaseMediumColor) || !TryParseColor(SystemBaseMediumColor, out var color)) return Color.Parse("#999999"); return color; } }
    public Color SystemBaseMediumLowColorPreview { get { if (string.IsNullOrWhiteSpace(SystemBaseMediumLowColor) || !TryParseColor(SystemBaseMediumLowColor, out var color)) return Color.Parse("#666666"); return color; } }
    public Color SystemBaseLowColorPreview { get { if (string.IsNullOrWhiteSpace(SystemBaseLowColor) || !TryParseColor(SystemBaseLowColor, out var color)) return Color.Parse("#333333"); return color; } }

    public Color SystemChromeAltLowColorPreview { get { if (string.IsNullOrWhiteSpace(SystemChromeAltLowColor) || !TryParseColor(SystemChromeAltLowColor, out var color)) return Color.Parse("#171717"); return color; } }
    public Color SystemChromeBlackHighColorPreview { get { if (string.IsNullOrWhiteSpace(SystemChromeBlackHighColor) || !TryParseColor(SystemChromeBlackHighColor, out var color)) return Color.Parse("#000000"); return color; } }
    public Color SystemChromeBlackLowColorPreview { get { if (string.IsNullOrWhiteSpace(SystemChromeBlackLowColor) || !TryParseColor(SystemChromeBlackLowColor, out var color)) return Color.Parse("#0D0D0D"); return color; } }
    public Color SystemChromeBlackMediumColorPreview { get { if (string.IsNullOrWhiteSpace(SystemChromeBlackMediumColor) || !TryParseColor(SystemChromeBlackMediumColor, out var color)) return Color.Parse("#1A1A1A"); return color; } }
    public Color SystemChromeBlackMediumLowColorPreview { get { if (string.IsNullOrWhiteSpace(SystemChromeBlackMediumLowColor) || !TryParseColor(SystemChromeBlackMediumLowColor, out var color)) return Color.Parse("#262626"); return color; } }
    public Color SystemChromeDisabledHighColorPreview { get { if (string.IsNullOrWhiteSpace(SystemChromeDisabledHighColor) || !TryParseColor(SystemChromeDisabledHighColor, out var color)) return Color.Parse("#666666"); return color; } }
    public Color SystemChromeDisabledLowColorPreview { get { if (string.IsNullOrWhiteSpace(SystemChromeDisabledLowColor) || !TryParseColor(SystemChromeDisabledLowColor, out var color)) return Color.Parse("#3D3D3D"); return color; } }
    public Color SystemChromeGrayColorPreview { get { if (string.IsNullOrWhiteSpace(SystemChromeGrayColor) || !TryParseColor(SystemChromeGrayColor, out var color)) return Color.Parse("#808080"); return color; } }
    public Color SystemChromeHighColorPreview { get { if (string.IsNullOrWhiteSpace(SystemChromeHighColor) || !TryParseColor(SystemChromeHighColor, out var color)) return Color.Parse("#767676"); return color; } }
    public Color SystemChromeLowColorPreview { get { if (string.IsNullOrWhiteSpace(SystemChromeLowColor) || !TryParseColor(SystemChromeLowColor, out var color)) return Color.Parse("#171717"); return color; } }
    public Color SystemChromeMediumColorPreview { get { if (string.IsNullOrWhiteSpace(SystemChromeMediumColor) || !TryParseColor(SystemChromeMediumColor, out var color)) return Color.Parse("#1F1F1F"); return color; } }
    public Color SystemChromeMediumLowColorPreview { get { if (string.IsNullOrWhiteSpace(SystemChromeMediumLowColor) || !TryParseColor(SystemChromeMediumLowColor, out var color)) return Color.Parse("#2B2B2B"); return color; } }
    public Color SystemChromeWhiteColorPreview { get { if (string.IsNullOrWhiteSpace(SystemChromeWhiteColor) || !TryParseColor(SystemChromeWhiteColor, out var color)) return Color.Parse("#FFFFFF"); return color; } }

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
    
    private ThemeHistoryViewModel? _historyViewModel = new();
    private Views.ThemeHistoryPanel? _historyPanel;
    
    private const int MaxHistorySize = 50; // Reasonable limit for undo/redo
    private static readonly string HistoryFilePath = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), 
        "Zer0Talk_ThemeEditor_History.json");
    
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
    public ICommand ToggleHistoryPanelCommand { get; }
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
    public ICommand ClearSystemAccentColorCommand { get; }
    public ICommand ClearSystemAccentColor2Command { get; }
    public ICommand ClearSystemAccentColor3Command { get; }
    public ICommand ClearSystemAccentColor4Command { get; }
    public ICommand ClearSystemAccentColorLightCommand { get; }
    public ICommand ClearSystemListLowColorCommand { get; }
    public ICommand ClearSystemListMediumColorCommand { get; }
    public ICommand ClearSystemAltHighColorCommand { get; }
    public ICommand ClearSystemAltMediumHighColorCommand { get; }
    public ICommand ClearSystemAltMediumColorCommand { get; }
    public ICommand ClearSystemAltMediumLowColorCommand { get; }
    public ICommand ClearSystemAltLowColorCommand { get; }
    public ICommand ClearSystemBaseHighColorCommand { get; }
    public ICommand ClearSystemBaseMediumHighColorCommand { get; }
    public ICommand ClearSystemBaseMediumColorCommand { get; }
    public ICommand ClearSystemBaseMediumLowColorCommand { get; }
    public ICommand ClearSystemBaseLowColorCommand { get; }
    public ICommand ClearSystemChromeAltLowColorCommand { get; }
    public ICommand ClearSystemChromeBlackHighColorCommand { get; }
    public ICommand ClearSystemChromeBlackLowColorCommand { get; }
    public ICommand ClearSystemChromeBlackMediumColorCommand { get; }
    public ICommand ClearSystemChromeBlackMediumLowColorCommand { get; }
    public ICommand ClearSystemChromeDisabledHighColorCommand { get; }
    public ICommand ClearSystemChromeDisabledLowColorCommand { get; }
    public ICommand ClearSystemChromeGrayColorCommand { get; }
    public ICommand ClearSystemChromeHighColorCommand { get; }
    public ICommand ClearSystemChromeLowColorCommand { get; }
    public ICommand ClearSystemChromeMediumColorCommand { get; }
    public ICommand ClearSystemChromeMediumLowColorCommand { get; }
    public ICommand ClearSystemChromeWhiteColorCommand { get; }
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
        // Load history from temp file
        LoadHistory();
        
        // Initialize commands
        EditColorCommand = new RelayCommand(param => StartEditingColor(param as ThemeColorEntry), param => param is ThemeColorEntry && !IsEditingColor);
        SaveColorEditCommand = new RelayCommand(async _ => await SaveColorEditAsync(), _ => IsEditingColor);
        CancelColorEditCommand = new RelayCommand(_ => CancelColorEdit(), _ => IsEditingColor);
        UndoColorEditCommand = new RelayCommand(_ => UndoColorEdit(), _ => CanUndo && !IsEditingColor);
        RedoColorEditCommand = new RelayCommand(_ => RedoColorEdit(), _ => CanRedo && !IsEditingColor);
        ToggleHistoryPanelCommand = new RelayCommand(_ => ToggleHistoryPanel());
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
        ClearSystemAccentColorCommand = new RelayCommand(_ => ClearSystemAccentColor());
        ClearSystemAccentColor2Command = new RelayCommand(_ => ClearSystemAccentColor2());
        ClearSystemAccentColor3Command = new RelayCommand(_ => ClearSystemAccentColor3());
        ClearSystemAccentColor4Command = new RelayCommand(_ => ClearSystemAccentColor4());
        ClearSystemAccentColorLightCommand = new RelayCommand(_ => ClearSystemAccentColorLight());
        ClearSystemListLowColorCommand = new RelayCommand(_ => ClearSystemListLowColor());
        ClearSystemListMediumColorCommand = new RelayCommand(_ => ClearSystemListMediumColor());
        ClearSystemAltHighColorCommand = new RelayCommand(_ => ClearSystemAltHighColor());
        ClearSystemAltMediumHighColorCommand = new RelayCommand(_ => ClearSystemAltMediumHighColor());
        ClearSystemAltMediumColorCommand = new RelayCommand(_ => ClearSystemAltMediumColor());
        ClearSystemAltMediumLowColorCommand = new RelayCommand(_ => ClearSystemAltMediumLowColor());
        ClearSystemAltLowColorCommand = new RelayCommand(_ => ClearSystemAltLowColor());
        ClearSystemBaseHighColorCommand = new RelayCommand(_ => ClearSystemBaseHighColor());
        ClearSystemBaseMediumHighColorCommand = new RelayCommand(_ => ClearSystemBaseMediumHighColor());
        ClearSystemBaseMediumColorCommand = new RelayCommand(_ => ClearSystemBaseMediumColor());
        ClearSystemBaseMediumLowColorCommand = new RelayCommand(_ => ClearSystemBaseMediumLowColor());
        ClearSystemBaseLowColorCommand = new RelayCommand(_ => ClearSystemBaseLowColor());
        ClearSystemChromeAltLowColorCommand = new RelayCommand(_ => ClearSystemChromeAltLowColor());
        ClearSystemChromeBlackHighColorCommand = new RelayCommand(_ => ClearSystemChromeBlackHighColor());
        ClearSystemChromeBlackLowColorCommand = new RelayCommand(_ => ClearSystemChromeBlackLowColor());
        ClearSystemChromeBlackMediumColorCommand = new RelayCommand(_ => ClearSystemChromeBlackMediumColor());
        ClearSystemChromeBlackMediumLowColorCommand = new RelayCommand(_ => ClearSystemChromeBlackMediumLowColor());
        ClearSystemChromeDisabledHighColorCommand = new RelayCommand(_ => ClearSystemChromeDisabledHighColor());
        ClearSystemChromeDisabledLowColorCommand = new RelayCommand(_ => ClearSystemChromeDisabledLowColor());
        ClearSystemChromeGrayColorCommand = new RelayCommand(_ => ClearSystemChromeGrayColor());
        ClearSystemChromeHighColorCommand = new RelayCommand(_ => ClearSystemChromeHighColor());
        ClearSystemChromeLowColorCommand = new RelayCommand(_ => ClearSystemChromeLowColor());
        ClearSystemChromeMediumColorCommand = new RelayCommand(_ => ClearSystemChromeMediumColor());
        ClearSystemChromeMediumLowColorCommand = new RelayCommand(_ => ClearSystemChromeMediumLowColor());
        ClearSystemChromeWhiteColorCommand = new RelayCommand(_ => ClearSystemChromeWhiteColor());
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
            dialog.AlphaSlider.Value = (byte)EditingAlpha;
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
                // Pull HSV and alpha from dialog and update editing state
                var (h, s, v) = dialog.GetHsv();
                var alpha = dialog.GetAlpha();
                EditingHue = h;
                EditingSaturation = s;
                EditingValue = v;
                EditingAlpha = alpha;
                // Convert to ARGB via helper
                var newColor = ColorFromHsv(EditingHue, EditingSaturation, EditingValue, alpha);
                var oldValue = entry.ColorValue;
                var newValue = ColorToHex(newColor);
                entry.ColorValue = newValue;
                
                // Record the change for undo/redo
                if (oldValue != newValue && ThemeDefinition.IsValidColorPublic(newValue))
                {
                    _undoStack.Push(new ColorEditAction
                    {
                        ResourceKey = entry.ResourceKey,
                        OldValue = oldValue,
                        NewValue = newValue
                    });
                    _redoStack.Clear();
                    
                    TrimHistoryIfNeeded();
                    SaveHistory();
                    UpdateHistoryPanel();
                    
                    AddToRecentColors(newValue);
                    Logger.Log($"[Theme Edit] Saved color edit '{entry.ResourceKey}': {oldValue} → {newValue}", LogLevel.Info, categoryOverride: "theme");
                    
                    OnPropertyChanged(nameof(CanUndo));
                    OnPropertyChanged(nameof(CanRedo));
                    (UndoColorEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (RedoColorEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    
                    // Apply live preview
                    try
                    {
                        var tempTheme = BuildThemeDefinition(preserveId: true);
                        if (AppServices.ThemeEngine.ApplyThemePreview(tempTheme))
                        {
                            Logger.Log($"[Theme Edit] Applied live preview for color '{entry.ResourceKey}'", LogLevel.Info, categoryOverride: "theme");
                        }
                    }
                    catch (Exception previewEx)
                    {
                        Logger.Log($"[Theme Edit] Failed to apply live preview: {previewEx.Message}", LogLevel.Warning, categoryOverride: "theme");
                    }
                }
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

            // Limit history size
            TrimHistoryIfNeeded();
            SaveHistory(); // Persist after edit
            UpdateHistoryPanel();

            AddToRecentColors(newValue);
            Logger.Log($"[Theme Edit] Saved color edit '{entry.ResourceKey}': {oldValue} → {newValue}", LogLevel.Info, categoryOverride: "theme");
            
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            (UndoColorEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RedoColorEditCommand as RelayCommand)?.RaiseCanExecuteChanged();

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

        // Try to find in ThemeColors collection first
        var entry = ThemeColors.FirstOrDefault(c => c.ResourceKey == action.ResourceKey);
        if (entry != null)
        {
            entry.ColorValue = action.OldValue;
            Logger.Log($"[Theme Edit] Undo: {action.ResourceKey} restored to {action.OldValue}", LogLevel.Info, categoryOverride: "theme");
        }
        else
        {
            // Handle system color properties
            SetSystemColorProperty(action.ResourceKey, action.OldValue);
            Logger.Log($"[Theme Edit] Undo: {action.ResourceKey} restored to {action.OldValue}", LogLevel.Info, categoryOverride: "theme");
        }

        SaveHistory(); // Persist after undo
        UpdateHistoryPanel();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        (UndoColorEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RedoColorEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void RedoColorEdit()
    {
        if (_redoStack.Count == 0) return;

        var action = _redoStack.Pop();
        _undoStack.Push(action);

        // Try to find in ThemeColors collection first
        var entry = ThemeColors.FirstOrDefault(c => c.ResourceKey == action.ResourceKey);
        if (entry != null)
        {
            entry.ColorValue = action.NewValue;
            Logger.Log($"[Theme Edit] Redo: {action.ResourceKey} changed to {action.NewValue}", LogLevel.Info, categoryOverride: "theme");
        }
        else
        {
            // Handle system color properties
            SetSystemColorProperty(action.ResourceKey, action.NewValue);
            Logger.Log($"[Theme Edit] Redo: {action.ResourceKey} changed to {action.NewValue}", LogLevel.Info, categoryOverride: "theme");
        }

        SaveHistory(); // Persist after redo
        UpdateHistoryPanel();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        (UndoColorEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RedoColorEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void SetSystemColorProperty(string propertyName, string value)
    {
        switch (propertyName)
        {
            case "SystemAccentColor": SystemAccentColor = value; break;
            case "SystemAccentColor2": SystemAccentColor2 = value; break;
            case "SystemAccentColor3": SystemAccentColor3 = value; break;
            case "SystemAccentColor4": SystemAccentColor4 = value; break;
            case "SystemAccentColorLight": SystemAccentColorLight = value; break;
            case "SystemListLowColor": SystemListLowColor = value; break;
            case "SystemListMediumColor": SystemListMediumColor = value; break;
            case "SystemAltHighColor": SystemAltHighColor = value; break;
            case "SystemAltMediumHighColor": SystemAltMediumHighColor = value; break;
            case "SystemAltMediumColor": SystemAltMediumColor = value; break;
            case "SystemAltMediumLowColor": SystemAltMediumLowColor = value; break;
            case "SystemAltLowColor": SystemAltLowColor = value; break;
            case "SystemBaseHighColor": SystemBaseHighColor = value; break;
            case "SystemBaseMediumHighColor": SystemBaseMediumHighColor = value; break;
            case "SystemBaseMediumColor": SystemBaseMediumColor = value; break;
            case "SystemBaseMediumLowColor": SystemBaseMediumLowColor = value; break;
            case "SystemBaseLowColor": SystemBaseLowColor = value; break;
            case "SystemChromeAltLowColor": SystemChromeAltLowColor = value; break;
            case "SystemChromeBlackHighColor": SystemChromeBlackHighColor = value; break;
            case "SystemChromeBlackLowColor": SystemChromeBlackLowColor = value; break;
            case "SystemChromeBlackMediumColor": SystemChromeBlackMediumColor = value; break;
            case "SystemChromeBlackMediumLowColor": SystemChromeBlackMediumLowColor = value; break;
            case "SystemChromeDisabledHighColor": SystemChromeDisabledHighColor = value; break;
            case "SystemChromeDisabledLowColor": SystemChromeDisabledLowColor = value; break;
            case "SystemChromeGrayColor": SystemChromeGrayColor = value; break;
            case "SystemChromeHighColor": SystemChromeHighColor = value; break;
            case "SystemChromeLowColor": SystemChromeLowColor = value; break;
            case "SystemChromeMediumColor": SystemChromeMediumColor = value; break;
            case "SystemChromeMediumLowColor": SystemChromeMediumLowColor = value; break;
            case "SystemChromeWhiteColor": SystemChromeWhiteColor = value; break;
            default:
                Logger.Log($"[Theme Edit] Unknown system color property: {propertyName}", LogLevel.Warning, categoryOverride: "theme");
                break;
        }
    }

    private void ToggleHistoryPanel()
    {
        if (_historyPanel == null)
        {
            _historyPanel = new Views.ThemeHistoryPanel
            {
                DataContext = _historyViewModel
            };
        }

        if (_historyPanel.IsVisible)
        {
            _historyPanel.Hide();
        }
        else
        {
            UpdateHistoryPanel();
            _historyPanel.Show();
        }
    }

    private void UpdateHistoryPanel()
    {
        if (_historyViewModel != null)
        {
            _historyViewModel.UpdateHistory(_undoStack, _redoStack, _undoStack.Count);
        }
    }

    public void CloseHistoryPanel()
    {
        if (_historyPanel != null && _historyPanel.IsVisible)
        {
            _historyPanel.Close();
        }
        _historyPanel = null;
    }

    #region History Persistence

    private void LoadHistory()
    {
        try
        {
            if (!System.IO.File.Exists(HistoryFilePath))
                return;

            var json = System.IO.File.ReadAllText(HistoryFilePath);
            var history = System.Text.Json.JsonSerializer.Deserialize<HistoryData>(json);

            if (history != null)
            {
                _undoStack.Clear();
                _redoStack.Clear();

                foreach (var action in history.UndoStack)
                    _undoStack.Push(action);

                foreach (var action in history.RedoStack)
                    _redoStack.Push(action);

                Logger.Log($"[Theme Edit] Loaded history: {_undoStack.Count} undo, {_redoStack.Count} redo", LogLevel.Info, categoryOverride: "theme");
                
                OnPropertyChanged(nameof(CanUndo));
                OnPropertyChanged(nameof(CanRedo));
                (UndoColorEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (RedoColorEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[Theme Edit] Failed to load history: {ex.Message}", LogLevel.Warning, categoryOverride: "theme");
        }
    }

    private void SaveHistory()
    {
        try
        {
            var history = new HistoryData
            {
                UndoStack = _undoStack.Reverse().ToList(),
                RedoStack = _redoStack.Reverse().ToList()
            };

            var json = System.Text.Json.JsonSerializer.Serialize(history, new System.Text.Json.JsonSerializerOptions 
            { 
                WriteIndented = true 
            });

            System.IO.File.WriteAllText(HistoryFilePath, json);
        }
        catch (Exception ex)
        {
            Logger.Log($"[Theme Edit] Failed to save history: {ex.Message}", LogLevel.Warning, categoryOverride: "theme");
        }
    }

    public void ClearHistory()
    {
        try
        {
            _undoStack.Clear();
            _redoStack.Clear();

            if (System.IO.File.Exists(HistoryFilePath))
            {
                System.IO.File.Delete(HistoryFilePath);
                Logger.Log("[Theme Edit] Cleared history file", LogLevel.Info, categoryOverride: "theme");
            }

            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            (UndoColorEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RedoColorEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            Logger.Log($"[Theme Edit] Failed to clear history: {ex.Message}", LogLevel.Warning, categoryOverride: "theme");
        }
    }

    private void TrimHistoryIfNeeded()
    {
        // Keep undo stack at reasonable size
        while (_undoStack.Count > MaxHistorySize)
        {
            // Remove oldest items (from bottom of stack)
            var temp = new System.Collections.Generic.Stack<ColorEditAction>(_undoStack.Reverse().Skip(1));
            _undoStack.Clear();
            foreach (var item in temp.Reverse())
                _undoStack.Push(item);
        }

        // Keep redo stack at reasonable size
        while (_redoStack.Count > MaxHistorySize)
        {
            var temp = new System.Collections.Generic.Stack<ColorEditAction>(_redoStack.Reverse().Skip(1));
            _redoStack.Clear();
            foreach (var item in temp.Reverse())
                _redoStack.Push(item);
        }
    }

    private class HistoryData
    {
        public System.Collections.Generic.List<ColorEditAction> UndoStack { get; set; } = new();
        public System.Collections.Generic.List<ColorEditAction> RedoStack { get; set; } = new();
    }

    #endregion

    #region Color Picker Helpers

    private void InitializeEditingState(string colorValue)
    {
        Logger.Log($"[ColorPicker] InitializeEditingState called with: '{colorValue}'", LogLevel.Debug, categoryOverride: "theme");
        
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
            
            Logger.Log($"[ColorPicker] Initialized - A:{color.A} R:{color.R} G:{color.G} B:{color.B} Hex:{_editingHex}", LogLevel.Debug, categoryOverride: "theme");
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

    private static string ColorToHex(Color color)
    {
        // Include alpha channel if not fully opaque
        if (color.A < 255)
        {
            return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

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

    #region System Accent Color Methods

    private void ClearSystemAccentColor()
    {
        SystemAccentColor = string.Empty;
        Logger.Log($"[Theme Edit] Cleared SystemAccentColor", LogLevel.Info, categoryOverride: "theme");
    }

    private void ClearSystemAccentColor2()
    {
        SystemAccentColor2 = string.Empty;
        Logger.Log($"[Theme Edit] Cleared SystemAccentColor2", LogLevel.Info, categoryOverride: "theme");
    }

    private void ClearSystemAccentColor3()
    {
        SystemAccentColor3 = string.Empty;
        Logger.Log($"[Theme Edit] Cleared SystemAccentColor3", LogLevel.Info, categoryOverride: "theme");
    }

    private void ClearSystemAccentColor4()
    {
        SystemAccentColor4 = string.Empty;
        Logger.Log($"[Theme Edit] Cleared SystemAccentColor4", LogLevel.Info, categoryOverride: "theme");
    }

    private void ClearSystemAccentColorLight()
    {
        SystemAccentColorLight = string.Empty;
        Logger.Log($"[Theme Edit] Cleared SystemAccentColorLight", LogLevel.Info, categoryOverride: "theme");
    }

    private void ClearSystemListLowColor()
    {
        SystemListLowColor = string.Empty;
        Logger.Log($"[Theme Edit] Cleared SystemListLowColor", LogLevel.Info, categoryOverride: "theme");
    }

    private void ClearSystemListMediumColor()
    {
        SystemListMediumColor = string.Empty;
        Logger.Log($"[Theme Edit] Cleared SystemListMediumColor", LogLevel.Info, categoryOverride: "theme");
    }

    private void ClearSystemAltHighColor()
    {
        SystemAltHighColor = string.Empty;
        Logger.Log($"[Theme Edit] Cleared SystemAltHighColor", LogLevel.Info, categoryOverride: "theme");
    }

    private void ClearSystemAltMediumHighColor()
    {
        SystemAltMediumHighColor = string.Empty;
        Logger.Log($"[Theme Edit] Cleared SystemAltMediumHighColor", LogLevel.Info, categoryOverride: "theme");
    }

    private void ClearSystemAltMediumColor()
    {
        SystemAltMediumColor = string.Empty;
        Logger.Log($"[Theme Edit] Cleared SystemAltMediumColor", LogLevel.Info, categoryOverride: "theme");
    }

    private void ClearSystemAltMediumLowColor()
    {
        SystemAltMediumLowColor = string.Empty;
        Logger.Log($"[Theme Edit] Cleared SystemAltMediumLowColor", LogLevel.Info, categoryOverride: "theme");
    }

    private void ClearSystemAltLowColor()
    {
        SystemAltLowColor = string.Empty;
        Logger.Log($"[Theme Edit] Cleared SystemAltLowColor", LogLevel.Info, categoryOverride: "theme");
    }

    private void ClearSystemBaseHighColor() { SystemBaseHighColor = string.Empty; Logger.Log($"[Theme Edit] Cleared SystemBaseHighColor", LogLevel.Info, categoryOverride: "theme"); }
    private void ClearSystemBaseMediumHighColor() { SystemBaseMediumHighColor = string.Empty; Logger.Log($"[Theme Edit] Cleared SystemBaseMediumHighColor", LogLevel.Info, categoryOverride: "theme"); }
    private void ClearSystemBaseMediumColor() { SystemBaseMediumColor = string.Empty; Logger.Log($"[Theme Edit] Cleared SystemBaseMediumColor", LogLevel.Info, categoryOverride: "theme"); }
    private void ClearSystemBaseMediumLowColor() { SystemBaseMediumLowColor = string.Empty; Logger.Log($"[Theme Edit] Cleared SystemBaseMediumLowColor", LogLevel.Info, categoryOverride: "theme"); }
    private void ClearSystemBaseLowColor() { SystemBaseLowColor = string.Empty; Logger.Log($"[Theme Edit] Cleared SystemBaseLowColor", LogLevel.Info, categoryOverride: "theme"); }
    private void ClearSystemChromeAltLowColor() { SystemChromeAltLowColor = string.Empty; Logger.Log($"[Theme Edit] Cleared SystemChromeAltLowColor", LogLevel.Info, categoryOverride: "theme"); }
    private void ClearSystemChromeBlackHighColor() { SystemChromeBlackHighColor = string.Empty; Logger.Log($"[Theme Edit] Cleared SystemChromeBlackHighColor", LogLevel.Info, categoryOverride: "theme"); }
    private void ClearSystemChromeBlackLowColor() { SystemChromeBlackLowColor = string.Empty; Logger.Log($"[Theme Edit] Cleared SystemChromeBlackLowColor", LogLevel.Info, categoryOverride: "theme"); }
    private void ClearSystemChromeBlackMediumColor() { SystemChromeBlackMediumColor = string.Empty; Logger.Log($"[Theme Edit] Cleared SystemChromeBlackMediumColor", LogLevel.Info, categoryOverride: "theme"); }
    private void ClearSystemChromeBlackMediumLowColor() { SystemChromeBlackMediumLowColor = string.Empty; Logger.Log($"[Theme Edit] Cleared SystemChromeBlackMediumLowColor", LogLevel.Info, categoryOverride: "theme"); }
    private void ClearSystemChromeDisabledHighColor() { SystemChromeDisabledHighColor = string.Empty; Logger.Log($"[Theme Edit] Cleared SystemChromeDisabledHighColor", LogLevel.Info, categoryOverride: "theme"); }
    private void ClearSystemChromeDisabledLowColor() { SystemChromeDisabledLowColor = string.Empty; Logger.Log($"[Theme Edit] Cleared SystemChromeDisabledLowColor", LogLevel.Info, categoryOverride: "theme"); }
    private void ClearSystemChromeGrayColor() { SystemChromeGrayColor = string.Empty; Logger.Log($"[Theme Edit] Cleared SystemChromeGrayColor", LogLevel.Info, categoryOverride: "theme"); }
    private void ClearSystemChromeHighColor() { SystemChromeHighColor = string.Empty; Logger.Log($"[Theme Edit] Cleared SystemChromeHighColor", LogLevel.Info, categoryOverride: "theme"); }
    private void ClearSystemChromeLowColor() { SystemChromeLowColor = string.Empty; Logger.Log($"[Theme Edit] Cleared SystemChromeLowColor", LogLevel.Info, categoryOverride: "theme"); }
    private void ClearSystemChromeMediumColor() { SystemChromeMediumColor = string.Empty; Logger.Log($"[Theme Edit] Cleared SystemChromeMediumColor", LogLevel.Info, categoryOverride: "theme"); }
    private void ClearSystemChromeMediumLowColor() { SystemChromeMediumLowColor = string.Empty; Logger.Log($"[Theme Edit] Cleared SystemChromeMediumLowColor", LogLevel.Info, categoryOverride: "theme"); }
    private void ClearSystemChromeWhiteColor() { SystemChromeWhiteColor = string.Empty; Logger.Log($"[Theme Edit] Cleared SystemChromeWhiteColor", LogLevel.Info, categoryOverride: "theme"); }

    public async void OpenSystemAccentColorPicker()
    {
        await OpenGenericColorPicker("SystemAccentColor", SystemAccentColor, "#0078D4", c => SystemAccentColor = c);
    }

    public async void OpenSystemAccentColor2Picker()
    {
        await OpenGenericColorPicker("SystemAccentColor2", SystemAccentColor2, "#005A9E", c => SystemAccentColor2 = c);
    }

    public async void OpenSystemAccentColor3Picker()
    {
        await OpenGenericColorPicker("SystemAccentColor3", SystemAccentColor3, "#106EBE", c => SystemAccentColor3 = c);
    }

    public async void OpenSystemAccentColor4Picker()
    {
        await OpenGenericColorPicker("SystemAccentColor4", SystemAccentColor4, "#0086F0", c => SystemAccentColor4 = c);
    }

    public async void OpenSystemAccentColorLightPicker()
    {
        await OpenGenericColorPicker("SystemAccentColorLight", SystemAccentColorLight, "#60CDFF", c => SystemAccentColorLight = c);
    }

    public async void OpenSystemListLowColorPicker()
    {
        await OpenGenericColorPicker("SystemListLowColor", SystemListLowColor, "#33FFFFFF", c => SystemListLowColor = c);
    }

    public async void OpenSystemListMediumColorPicker()
    {
        await OpenGenericColorPicker("SystemListMediumColor", SystemListMediumColor, "#66FFFFFF", c => SystemListMediumColor = c);
    }

    public async void OpenSystemAltHighColorPicker()
    {
        try
        {
            var dialog = new Zer0Talk.Controls.ColorPicker.ColorPickerDialog();
            var currentColor = !string.IsNullOrWhiteSpace(SystemAltHighColor) ? SystemAltHighColor : "#FFFFFFFF";
            InitializeEditingState(currentColor);
            
            dialog.SvPicker.Hue = EditingHue;
            dialog.SvPicker.Saturation = EditingSaturation;
            dialog.SvPicker.Value = EditingValue;
            dialog.HueSlider.Hue = EditingHue;
            dialog.BrightnessSlider.Brightness = EditingValue;
            dialog.RedSlider.Value = (byte)EditingRed;
            dialog.GreenSlider.Value = (byte)EditingGreen;
            dialog.BlueSlider.Value = (byte)EditingBlue;
            dialog.HexInput.Text = EditingHex;

            Window? owner = null;
            if (App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                owner = desktop.MainWindow;
            }

            var result = await dialog.ShowDialog<bool?>(owner!);
            if (result == true)
            {
                var (h, s, v) = dialog.GetHsv();
                EditingHue = h;
                EditingSaturation = s;
                EditingValue = v;
                var newColor = ColorFromHsv(EditingHue, EditingSaturation, EditingValue, (byte)EditingAlpha);
                SystemAltHighColor = ColorToHex(newColor);
            }

            Logger.Log($"[Theme Edit] SystemAltHighColor picker closed (applied: {result})", LogLevel.Info, categoryOverride: "theme");
        }
        catch (Exception ex)
        {
            Logger.Log($"[Theme Edit] Failed to open SystemAltHighColor picker: {ex.Message}", LogLevel.Error, categoryOverride: "theme");
        }
    }

    public async void OpenSystemAltMediumHighColorPicker()
    {
        await OpenGenericColorPicker("SystemAltMediumHighColor", SystemAltMediumHighColor, "#CCFFFFFF", c => SystemAltMediumHighColor = c);
    }

    public async void OpenSystemAltMediumColorPicker()
    {
        await OpenGenericColorPicker("SystemAltMediumColor", SystemAltMediumColor, "#99FFFFFF", c => SystemAltMediumColor = c);
    }

    public async void OpenSystemAltMediumLowColorPicker()
    {
        await OpenGenericColorPicker("SystemAltMediumLowColor", SystemAltMediumLowColor, "#66FFFFFF", c => SystemAltMediumLowColor = c);
    }

    public async void OpenSystemAltLowColorPicker()
    {
        await OpenGenericColorPicker("SystemAltLowColor", SystemAltLowColor, "#33FFFFFF", c => SystemAltLowColor = c);
    }

    // SystemBase color pickers
    public async void OpenSystemBaseHighColorPicker() { await OpenGenericColorPicker("SystemBaseHighColor", SystemBaseHighColor, "#FFFFFF", c => SystemBaseHighColor = c); }
    public async void OpenSystemBaseMediumHighColorPicker() { await OpenGenericColorPicker("SystemBaseMediumHighColor", SystemBaseMediumHighColor, "#CCCCCC", c => SystemBaseMediumHighColor = c); }
    public async void OpenSystemBaseMediumColorPicker() { await OpenGenericColorPicker("SystemBaseMediumColor", SystemBaseMediumColor, "#999999", c => SystemBaseMediumColor = c); }
    public async void OpenSystemBaseMediumLowColorPicker() { await OpenGenericColorPicker("SystemBaseMediumLowColor", SystemBaseMediumLowColor, "#666666", c => SystemBaseMediumLowColor = c); }
    public async void OpenSystemBaseLowColorPicker() { await OpenGenericColorPicker("SystemBaseLowColor", SystemBaseLowColor, "#333333", c => SystemBaseLowColor = c); }

    // SystemChrome color pickers
    public async void OpenSystemChromeAltLowColorPicker() { await OpenGenericColorPicker("SystemChromeAltLowColor", SystemChromeAltLowColor, "#171717", c => SystemChromeAltLowColor = c); }
    public async void OpenSystemChromeBlackHighColorPicker() { await OpenGenericColorPicker("SystemChromeBlackHighColor", SystemChromeBlackHighColor, "#000000", c => SystemChromeBlackHighColor = c); }
    public async void OpenSystemChromeBlackLowColorPicker() { await OpenGenericColorPicker("SystemChromeBlackLowColor", SystemChromeBlackLowColor, "#0D0D0D", c => SystemChromeBlackLowColor = c); }
    public async void OpenSystemChromeBlackMediumColorPicker() { await OpenGenericColorPicker("SystemChromeBlackMediumColor", SystemChromeBlackMediumColor, "#1A1A1A", c => SystemChromeBlackMediumColor = c); }
    public async void OpenSystemChromeBlackMediumLowColorPicker() { await OpenGenericColorPicker("SystemChromeBlackMediumLowColor", SystemChromeBlackMediumLowColor, "#262626", c => SystemChromeBlackMediumLowColor = c); }
    public async void OpenSystemChromeDisabledHighColorPicker() { await OpenGenericColorPicker("SystemChromeDisabledHighColor", SystemChromeDisabledHighColor, "#666666", c => SystemChromeDisabledHighColor = c); }
    public async void OpenSystemChromeDisabledLowColorPicker() { await OpenGenericColorPicker("SystemChromeDisabledLowColor", SystemChromeDisabledLowColor, "#3D3D3D", c => SystemChromeDisabledLowColor = c); }
    public async void OpenSystemChromeGrayColorPicker() { await OpenGenericColorPicker("SystemChromeGrayColor", SystemChromeGrayColor, "#808080", c => SystemChromeGrayColor = c); }
    public async void OpenSystemChromeHighColorPicker() { await OpenGenericColorPicker("SystemChromeHighColor", SystemChromeHighColor, "#767676", c => SystemChromeHighColor = c); }
    public async void OpenSystemChromeLowColorPicker() { await OpenGenericColorPicker("SystemChromeLowColor", SystemChromeLowColor, "#171717", c => SystemChromeLowColor = c); }
    public async void OpenSystemChromeMediumColorPicker() { await OpenGenericColorPicker("SystemChromeMediumColor", SystemChromeMediumColor, "#1F1F1F", c => SystemChromeMediumColor = c); }
    public async void OpenSystemChromeMediumLowColorPicker() { await OpenGenericColorPicker("SystemChromeMediumLowColor", SystemChromeMediumLowColor, "#2B2B2B", c => SystemChromeMediumLowColor = c); }
    public async void OpenSystemChromeWhiteColorPicker() { await OpenGenericColorPicker("SystemChromeWhiteColor", SystemChromeWhiteColor, "#FFFFFF", c => SystemChromeWhiteColor = c); }

    private async Task OpenGenericColorPicker(string colorName, string? currentValue, string defaultValue, Action<string> setter)
    {
        try
        {
            var dialog = new Zer0Talk.Controls.ColorPicker.ColorPickerDialog();
            var currentColor = !string.IsNullOrWhiteSpace(currentValue) ? currentValue : defaultValue;
            var oldValue = currentColor;
            InitializeEditingState(currentColor);
            
            dialog.SvPicker.Hue = EditingHue;
            dialog.SvPicker.Saturation = EditingSaturation;
            dialog.SvPicker.Value = EditingValue;
            dialog.HueSlider.Hue = EditingHue;
            dialog.BrightnessSlider.Brightness = EditingValue;
            dialog.RedSlider.Value = (byte)EditingRed;
            dialog.GreenSlider.Value = (byte)EditingGreen;
            dialog.BlueSlider.Value = (byte)EditingBlue;
            dialog.AlphaSlider.Value = (byte)EditingAlpha;
            dialog.HexInput.Text = EditingHex;

            Window? owner = null;
            if (App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                owner = desktop.MainWindow;
            }

            var result = await dialog.ShowDialog<bool?>(owner!);
            if (result == true)
            {
                var (h, s, v) = dialog.GetHsv();
                var alpha = dialog.GetAlpha();
                EditingHue = h;
                EditingSaturation = s;
                EditingValue = v;
                EditingAlpha = alpha;
                var newColor = ColorFromHsv(EditingHue, EditingSaturation, EditingValue, alpha);
                var newValue = ColorToHex(newColor);
                setter(newValue);
                
                // Record the change for undo/redo
                if (oldValue != newValue)
                {
                    _undoStack.Push(new ColorEditAction
                    {
                        ResourceKey = colorName,
                        OldValue = oldValue,
                        NewValue = newValue
                    });
                    _redoStack.Clear();
                    
                    TrimHistoryIfNeeded();
                    SaveHistory();
                    UpdateHistoryPanel();
                    
                    Logger.Log($"[Theme Edit] Saved {colorName} edit: {oldValue} → {newValue}", LogLevel.Info, categoryOverride: "theme");
                    
                    OnPropertyChanged(nameof(CanUndo));
                    OnPropertyChanged(nameof(CanRedo));
                    (UndoColorEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (RedoColorEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }

            Logger.Log($"[Theme Edit] {colorName} picker closed (applied: {result})", LogLevel.Info, categoryOverride: "theme");
        }
        catch (Exception ex)
        {
            Logger.Log($"[Theme Edit] Failed to open {colorName} picker: {ex.Message}", LogLevel.Error, categoryOverride: "theme");
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
            
            // Load system accent colors
            SystemAccentColor = blank.SystemAccentColor;
            SystemAccentColor2 = blank.SystemAccentColor2;
            SystemAccentColor3 = blank.SystemAccentColor3;
            SystemAccentColor4 = blank.SystemAccentColor4;
            SystemAccentColorLight = blank.SystemAccentColorLight;
            SystemListLowColor = blank.SystemListLowColor;
            SystemListMediumColor = blank.SystemListMediumColor;
            SystemAltHighColor = blank.SystemAltHighColor;
            SystemAltMediumHighColor = blank.SystemAltMediumHighColor;
            SystemAltMediumColor = blank.SystemAltMediumColor;
            SystemAltMediumLowColor = blank.SystemAltMediumLowColor;
            SystemAltLowColor = blank.SystemAltLowColor;
            SystemBaseHighColor = blank.SystemBaseHighColor;
            SystemBaseMediumHighColor = blank.SystemBaseMediumHighColor;
            SystemBaseMediumColor = blank.SystemBaseMediumColor;
            SystemBaseMediumLowColor = blank.SystemBaseMediumLowColor;
            SystemBaseLowColor = blank.SystemBaseLowColor;
            SystemChromeAltLowColor = blank.SystemChromeAltLowColor;
            SystemChromeBlackHighColor = blank.SystemChromeBlackHighColor;
            SystemChromeBlackLowColor = blank.SystemChromeBlackLowColor;
            SystemChromeBlackMediumColor = blank.SystemChromeBlackMediumColor;
            SystemChromeBlackMediumLowColor = blank.SystemChromeBlackMediumLowColor;
            SystemChromeDisabledHighColor = blank.SystemChromeDisabledHighColor;
            SystemChromeDisabledLowColor = blank.SystemChromeDisabledLowColor;
            SystemChromeGrayColor = blank.SystemChromeGrayColor;
            SystemChromeHighColor = blank.SystemChromeHighColor;
            SystemChromeLowColor = blank.SystemChromeLowColor;
            SystemChromeMediumColor = blank.SystemChromeMediumColor;
            SystemChromeMediumLowColor = blank.SystemChromeMediumLowColor;
            SystemChromeWhiteColor = blank.SystemChromeWhiteColor;
            
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
            
            // Load system accent colors
            SystemAccentColor = themeDef.SystemAccentColor;
            SystemAccentColor2 = themeDef.SystemAccentColor2;
            SystemAccentColor3 = themeDef.SystemAccentColor3;
            SystemAccentColor4 = themeDef.SystemAccentColor4;
            SystemAccentColorLight = themeDef.SystemAccentColorLight;
            SystemListLowColor = themeDef.SystemListLowColor;
            SystemListMediumColor = themeDef.SystemListMediumColor;
            SystemAltHighColor = themeDef.SystemAltHighColor;
            SystemAltMediumHighColor = themeDef.SystemAltMediumHighColor;
            SystemAltMediumColor = themeDef.SystemAltMediumColor;
            SystemAltMediumLowColor = themeDef.SystemAltMediumLowColor;
            SystemAltLowColor = themeDef.SystemAltLowColor;
            SystemBaseHighColor = themeDef.SystemBaseHighColor;
            SystemBaseMediumHighColor = themeDef.SystemBaseMediumHighColor;
            SystemBaseMediumColor = themeDef.SystemBaseMediumColor;
            SystemBaseMediumLowColor = themeDef.SystemBaseMediumLowColor;
            SystemBaseLowColor = themeDef.SystemBaseLowColor;
            SystemChromeAltLowColor = themeDef.SystemChromeAltLowColor;
            SystemChromeBlackHighColor = themeDef.SystemChromeBlackHighColor;
            SystemChromeBlackLowColor = themeDef.SystemChromeBlackLowColor;
            SystemChromeBlackMediumColor = themeDef.SystemChromeBlackMediumColor;
            SystemChromeBlackMediumLowColor = themeDef.SystemChromeBlackMediumLowColor;
            SystemChromeDisabledHighColor = themeDef.SystemChromeDisabledHighColor;
            SystemChromeDisabledLowColor = themeDef.SystemChromeDisabledLowColor;
            SystemChromeGrayColor = themeDef.SystemChromeGrayColor;
            SystemChromeHighColor = themeDef.SystemChromeHighColor;
            SystemChromeLowColor = themeDef.SystemChromeLowColor;
            SystemChromeMediumColor = themeDef.SystemChromeMediumColor;
            SystemChromeMediumLowColor = themeDef.SystemChromeMediumLowColor;
            SystemChromeWhiteColor = themeDef.SystemChromeWhiteColor;
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
            // System accent color overrides
            SystemAccentColor = SystemAccentColor,
            SystemAccentColor2 = SystemAccentColor2,
            SystemAccentColor3 = SystemAccentColor3,
            SystemAccentColor4 = SystemAccentColor4,
            SystemAccentColorLight = SystemAccentColorLight,
            SystemListLowColor = SystemListLowColor,
            SystemListMediumColor = SystemListMediumColor,
            SystemAltHighColor = SystemAltHighColor,
            SystemAltMediumHighColor = SystemAltMediumHighColor,
            SystemAltMediumColor = SystemAltMediumColor,
            SystemAltMediumLowColor = SystemAltMediumLowColor,
            SystemAltLowColor = SystemAltLowColor,
            SystemBaseHighColor = SystemBaseHighColor,
            SystemBaseMediumHighColor = SystemBaseMediumHighColor,
            SystemBaseMediumColor = SystemBaseMediumColor,
            SystemBaseMediumLowColor = SystemBaseMediumLowColor,
            SystemBaseLowColor = SystemBaseLowColor,
            SystemChromeAltLowColor = SystemChromeAltLowColor,
            SystemChromeBlackHighColor = SystemChromeBlackHighColor,
            SystemChromeBlackLowColor = SystemChromeBlackLowColor,
            SystemChromeBlackMediumColor = SystemChromeBlackMediumColor,
            SystemChromeBlackMediumLowColor = SystemChromeBlackMediumLowColor,
            SystemChromeDisabledHighColor = SystemChromeDisabledHighColor,
            SystemChromeDisabledLowColor = SystemChromeDisabledLowColor,
            SystemChromeGrayColor = SystemChromeGrayColor,
            SystemChromeHighColor = SystemChromeHighColor,
            SystemChromeLowColor = SystemChromeLowColor,
            SystemChromeMediumColor = SystemChromeMediumColor,
            SystemChromeMediumLowColor = SystemChromeMediumLowColor,
            SystemChromeWhiteColor = SystemChromeWhiteColor,
            ColorOverrides = new System.Collections.Generic.Dictionary<string, string>(),
            Gradients = new System.Collections.Generic.Dictionary<string, GradientDefinition>()
        };

        foreach (var colorEntry in ThemeColors)
        {
            theme.ColorOverrides[colorEntry.ResourceKey] = colorEntry.ColorValue;
            
            // Automatically sync FluentTheme's ListBoxItem hover brush with App.ItemHover
            if (colorEntry.ResourceKey == "App.ItemHover")
            {
                theme.ColorOverrides["SystemControlHighlightListLowBrush"] = colorEntry.ColorValue;
            }
            
            // Automatically sync FluentTheme's ListBoxItem selected brush with App.ItemSelected
            if (colorEntry.ResourceKey == "App.ItemSelected")
            {
                theme.ColorOverrides["SystemControlHighlightListAccentLowBrush"] = colorEntry.ColorValue;
            }
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
            Logger.Log("[Theme Search] Opening search dialog...", LogLevel.Info, categoryOverride: "theme");

            var dialog = new Views.ThemeSearchDialog
            {
                DataContext = new ThemeSearchViewModel()
            };

            var viewModel = dialog.DataContext as ThemeSearchViewModel;

            // Start the search in the background
            if (viewModel != null)
            {
                _ = Task.Run(async () =>
                {
                    await viewModel.StartSearchAsync();
                });
            }

            // Show dialog modally
            var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is 
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow 
                : null;
            
            if (mainWindow != null)
            {
                await dialog.ShowDialog(mainWindow);
            }
            else
            {
                dialog.Show();
            }

            Logger.Log("[Theme Search] Search dialog closed", LogLevel.Info, categoryOverride: "theme");
        }
        catch (Exception ex)
        {
            Logger.Log($"[Theme Search] Error opening search dialog: {ex.Message}", LogLevel.Error, categoryOverride: "theme");
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

    public class ColorEditAction
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
