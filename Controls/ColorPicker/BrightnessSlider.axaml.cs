using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace ZTalk.Controls.ColorPicker;

public partial class BrightnessSlider : UserControl
{
    public static readonly StyledProperty<double> BrightnessProperty =
        AvaloniaProperty.Register<BrightnessSlider, double>(nameof(Brightness), 1.0, coerce: CoerceBrightness);

    static BrightnessSlider()
    {
        BrightnessProperty.Changed.AddClassHandler<BrightnessSlider>((slider, _) =>
        {
            slider.UpdateThumbPosition();
            slider.RaiseBrightnessChanged();
        });
    }

    private Canvas? _thumbCanvas;
    private Border? _thumb;
    private bool _isPointerCaptured;

    public BrightnessSlider()
    {
        InitializeComponent();
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += (_, _) => _isPointerCaptured = false;
        LayoutUpdated += (_, _) => UpdateThumbPosition();
    }

    public double Brightness
    {
        get => GetValue(BrightnessProperty);
        set => SetValue(BrightnessProperty, value);
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
        var brightness = 1.0 - (y / height); // Top = 1.0 (white), Bottom = 0.0 (black)

        SetCurrentValue(BrightnessProperty, brightness);
    }

    public event EventHandler? BrightnessChanged;
    private void RaiseBrightnessChanged()
    {
        BrightnessChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateThumbPosition()
    {
        if (_thumbCanvas == null || _thumb == null) return;
        var height = _thumbCanvas.Bounds.Height;
        if (height <= 0) return;

        var y = (1.0 - Brightness) * height;
        Canvas.SetTop(_thumb, y - _thumb.Bounds.Height / 2);
    }

    private static double CoerceBrightness(AvaloniaObject sender, double value)
    {
        return Math.Clamp(value, 0.0, 1.0);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        const double small = 0.01;
        const double large = 0.05;
        var step = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? large : small;

        switch (e.Key)
        {
            case Key.Up:
                Brightness = Math.Min(1.0, Brightness + step);
                e.Handled = true;
                break;
            case Key.Down:
                Brightness = Math.Max(0.0, Brightness - step);
                e.Handled = true;
                break;
        }
    }
}
