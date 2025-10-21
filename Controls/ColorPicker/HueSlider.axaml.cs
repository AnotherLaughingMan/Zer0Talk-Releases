using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace Zer0Talk.Controls.ColorPicker;

public partial class HueSlider : UserControl
{
    public static readonly StyledProperty<double> HueProperty =
        AvaloniaProperty.Register<HueSlider, double>(nameof(Hue), 0d, coerce: CoerceHue);

    static HueSlider()
    {
        HueProperty.Changed.AddClassHandler<HueSlider>((slider, _) =>
        {
            slider.UpdateThumbPosition();
            slider.RaiseHueChanged();
        });
    }

    private Canvas? _thumbCanvas;
    private Border? _thumb;
    private bool _isPointerCaptured;

    public HueSlider()
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

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _thumbCanvas = this.FindControl<Canvas>("ThumbCanvas");
        _thumb = this.FindControl<Border>("Thumb");
        UpdateThumbPosition();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_thumbCanvas == null) return;
        var point = e.GetPosition(_thumbCanvas);
        UpdateFromPoint(point);
        _isPointerCaptured = true;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPointerCaptured || _thumbCanvas == null) return;
        var point = e.GetPosition(_thumbCanvas);
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
        if (_thumbCanvas == null) return;

        var height = _thumbCanvas.Bounds.Height;
        if (height <= 0) return;

        var y = Math.Clamp(point.Y, 0, height);
        var hue = (y / height) * 360.0;

        SetCurrentValue(HueProperty, hue);
    }

    public event EventHandler? HueChanged;
    private void RaiseHueChanged()
    {
        HueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateThumbPosition()
    {
        if (_thumbCanvas == null || _thumb == null) return;
        var height = _thumbCanvas.Bounds.Height;
        if (height <= 0) return;

        var y = (Hue / 360.0) * height;
        Canvas.SetTop(_thumb, y - _thumb.Bounds.Height / 2);
    }

    private static double CoerceHue(AvaloniaObject sender, double value)
    {
        // Normalize hue to 0-360 range
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 0;

        var normalized = value % 360.0;
        if (normalized < 0)
            normalized += 360.0;

        return normalized;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        const double small = 1.0;
        const double large = 10.0;
        var step = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? large : small;

        switch (e.Key)
        {
            case Key.Up:
                Hue = Math.Max(0, Hue - step);
                e.Handled = true;
                break;
            case Key.Down:
                Hue = Math.Min(360, Hue + step);
                e.Handled = true;
                break;
        }
    }
}
