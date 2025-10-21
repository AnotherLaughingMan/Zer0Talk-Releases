using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Zer0Talk.Controls.ColorPicker;

public partial class ColorChannelSlider : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<ColorChannelSlider, string>(nameof(Label), "Channel");

    public static readonly StyledProperty<byte> ValueProperty =
        AvaloniaProperty.Register<ColorChannelSlider, byte>(nameof(Value), 0);

    public static readonly StyledProperty<Color> StartColorProperty =
        AvaloniaProperty.Register<ColorChannelSlider, Color>(nameof(StartColor), Colors.Black);

    public static readonly StyledProperty<Color> EndColorProperty =
        AvaloniaProperty.Register<ColorChannelSlider, Color>(nameof(EndColor), Colors.Red);

    static ColorChannelSlider()
    {
        LabelProperty.Changed.AddClassHandler<ColorChannelSlider>((slider, e) => slider.UpdateLabel());
        ValueProperty.Changed.AddClassHandler<ColorChannelSlider>((slider, e) => slider.UpdateFromValue());
        StartColorProperty.Changed.AddClassHandler<ColorChannelSlider>((slider, e) => slider.UpdateGradient());
        EndColorProperty.Changed.AddClassHandler<ColorChannelSlider>((slider, e) => slider.UpdateGradient());
    }

    private TextBlock? _labelText;
    private Slider? _valueSlider;
    private TextBox? _valueInput;
    private Border? _gradientBar;
    private Canvas? _thumbCanvas;
    private Border? _thumbIndicator;
    private bool _updatingFromSlider;
    private bool _updatingFromInput;

    public ColorChannelSlider()
    {
        InitializeComponent();
        LayoutUpdated += (_, _) => UpdateThumbPosition();
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public byte Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Color StartColor
    {
        get => GetValue(StartColorProperty);
        set => SetValue(StartColorProperty, value);
    }

    public Color EndColor
    {
        get => GetValue(EndColorProperty);
        set => SetValue(EndColorProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        
        _labelText = this.FindControl<TextBlock>("LabelText");
        _valueSlider = this.FindControl<Slider>("ValueSlider");
        _valueInput = this.FindControl<TextBox>("ValueInput");
        _gradientBar = this.FindControl<Border>("GradientBar");
        _thumbCanvas = this.FindControl<Canvas>("ThumbCanvas");
        _thumbIndicator = this.FindControl<Border>("ThumbIndicator");

        if (_valueSlider != null)
        {
            _valueSlider.PropertyChanged += (_, e) =>
            {
                if (e.Property.Name == "Value" && !_updatingFromInput)
                {
                    _updatingFromSlider = true;
                    var newValue = (byte)Math.Round(_valueSlider.Value);
                    SetCurrentValue(ValueProperty, newValue);
                    _updatingFromSlider = false;
                }
            };
        }

        if (_valueInput != null)
        {
            ByteTextBoxHelper.SetEnableByteInput(_valueInput, true);
            _valueInput.LostFocus += (_, _) =>
            {
                if (!_updatingFromSlider && byte.TryParse(_valueInput.Text, out var newValue))
                {
                    _updatingFromInput = true;
                    SetCurrentValue(ValueProperty, newValue);
                    _updatingFromInput = false;
                }
            };
        }

        UpdateLabel();
        UpdateGradient();
        UpdateFromValue();
    }

    public event EventHandler? ValueChanged;

    private void UpdateLabel()
    {
        if (_labelText != null)
            _labelText.Text = Label;
    }

    private void UpdateFromValue()
    {
        if (!_updatingFromSlider && _valueSlider != null)
            _valueSlider.Value = Value;

        if (!_updatingFromInput && _valueInput != null)
            _valueInput.Text = Value.ToString();

        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateGradient()
    {
        if (_gradientBar?.Background is LinearGradientBrush brush && brush.GradientStops.Count >= 2)
        {
            brush.GradientStops[0].Color = StartColor;
            brush.GradientStops[1].Color = EndColor;
        }
    }

    private void UpdateThumbPosition()
    {
        if (_thumbCanvas == null || _thumbIndicator == null || _valueSlider == null) return;
        
        var width = _thumbCanvas.Bounds.Width;
        if (width <= 0) return;

        var position = (Value / 255.0) * width;
        Canvas.SetLeft(_thumbIndicator, position - (_thumbIndicator.Bounds.Width / 2));
    }
}
