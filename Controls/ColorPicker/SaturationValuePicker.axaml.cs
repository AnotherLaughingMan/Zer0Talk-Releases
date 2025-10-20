using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;

namespace ZTalk.Controls.ColorPicker;

public partial class SaturationValuePicker : UserControl
{
    public static readonly StyledProperty<double> HueProperty =
        AvaloniaProperty.Register<SaturationValuePicker, double>(nameof(Hue), 0d);

    public static readonly StyledProperty<double> SaturationProperty =
        AvaloniaProperty.Register<SaturationValuePicker, double>(nameof(Saturation), 1d);

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<SaturationValuePicker, double>(nameof(Value), 1d);

    static SaturationValuePicker()
    {
        HueProperty.Changed.AddClassHandler<SaturationValuePicker>((picker, _) => 
        { 
            picker.UpdateHueBackground(); 
            picker.RaiseHsvChanged(); 
        });
        SaturationProperty.Changed.AddClassHandler<SaturationValuePicker>((picker, _) => 
        { 
            picker.UpdateThumbPosition(); 
            picker.RaiseHsvChanged(); 
        });
        ValueProperty.Changed.AddClassHandler<SaturationValuePicker>((picker, _) => 
        { 
            picker.UpdateThumbPosition(); 
            picker.RaiseHsvChanged(); 
        });
    }

    private Border? _hueLayer;
    private Ellipse? _thumb;
    private Grid? _root;
    private bool _isPointerCaptured;

    public SaturationValuePicker()
    {
        InitializeComponent();
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += (_, _) => _isPointerCaptured = false;
        LayoutUpdated += (_, _) => UpdateThumbPosition();
    }

    public double Hue
    {
        get => GetValue(HueProperty);
        set => SetValue(HueProperty, value);
    }

    public double Saturation
    {
        get => GetValue(SaturationProperty);
        set => SetValue(SaturationProperty, Math.Clamp(value, 0d, 1d));
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, Math.Clamp(value, 0d, 1d));
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _root = this.FindControl<Grid>("Root");
        _hueLayer = this.FindControl<Border>("HueLayer");
        _thumb = this.FindControl<Ellipse>("Thumb");
        UpdateHueBackground();
        UpdateThumbPosition();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_root == null) return;
        var point = e.GetPosition(_root);
        UpdateFromPoint(point);
        _isPointerCaptured = true;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPointerCaptured || _root == null) return;
        var point = e.GetPosition(_root);
        UpdateFromPoint(point);
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPointerCaptured) return;
        _isPointerCaptured = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void UpdateFromPoint(Point point)
    {
        if (_root == null) return;

        var width = _root.Bounds.Width;
        var height = _root.Bounds.Height;
        if (width <= 0 || height <= 0) return;

        var x = Math.Clamp(point.X, 0, width);
        var y = Math.Clamp(point.Y, 0, height);

        var saturation = x / width;
        var value = 1 - (y / height);

        SetCurrentValue(SaturationProperty, saturation);
        SetCurrentValue(ValueProperty, value);
    }

    public event EventHandler? HsvChanged;
    private void RaiseHsvChanged()
    {
        HsvChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateHueBackground()
    {
        if (_hueLayer == null) return;

        // Generate the pure hue color at full saturation and value
        var hueColor = ColorUtils.ColorFromHsv(Hue, 1.0, 1.0, 255);
        _hueLayer.Background = new SolidColorBrush(hueColor);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        const double small = 0.01;
        const double large = 0.05;
        var step = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? large : small;

        switch (e.Key)
        {
            case Key.Left:
                Saturation = Math.Max(0, Saturation - step);
                e.Handled = true;
                break;
            case Key.Right:
                Saturation = Math.Min(1, Saturation + step);
                e.Handled = true;
                break;
            case Key.Up:
                Value = Math.Min(1, Value + step);
                e.Handled = true;
                break;
            case Key.Down:
                Value = Math.Max(0, Value - step);
                e.Handled = true;
                break;
        }
    }

    private void UpdateThumbPosition()
    {
        if (_root == null || _thumb == null) return;
        var width = _root.Bounds.Width;
        var height = _root.Bounds.Height;
        if (width <= 0 || height <= 0) return;

        var x = Saturation * width;
        var y = (1 - Value) * height;

        Canvas.SetLeft(_thumb, x - _thumb.Bounds.Width / 2);
        Canvas.SetTop(_thumb, y - _thumb.Bounds.Height / 2);
    }
}
