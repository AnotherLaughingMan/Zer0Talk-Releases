using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Zer0Talk.Controls.ColorPicker
{
    using System.Reactive.Disposables;
    using System.Reactive.Linq;

    public partial class ColorPickerDialog : Window
    {
        private readonly CompositeDisposable _disposables = new();
        private bool _isUpdating;

        public ColorPickerDialog()
        {
            InitializeComponent();
            
            // Wire up buttons
            ApplyBtn.Click += (_, _) => Close(true);
            CancelBtn.Click += (_, _) => Close(false);

            // Wire up all controls to sync with each other
            HueSlider.HueChanged += (_, _) => { if (!_isUpdating) OnHsvChanged(); };
            SvPicker.HsvChanged += (_, _) => { if (!_isUpdating) OnHsvChanged(); };
            BrightnessSlider.BrightnessChanged += (_, _) => { if (!_isUpdating) OnBrightnessChanged(); };
            
            RedSlider.ValueChanged += (_, _) => { if (!_isUpdating) OnRgbChanged(); };
            GreenSlider.ValueChanged += (_, _) => { if (!_isUpdating) OnRgbChanged(); };
            BlueSlider.ValueChanged += (_, _) => { if (!_isUpdating) OnRgbChanged(); };
            AlphaSlider.ValueChanged += (_, _) => { if (!_isUpdating) OnAlphaChanged(); };
            
            // Hex input handling with throttle
            var hexObs = HexInput.GetObservable(TextBox.TextProperty);
            
            var pendingSub = hexObs.Subscribe(_ => 
                Avalonia.Threading.Dispatcher.UIThread.Post(() => 
                    PendingIndicator.IsVisible = true));
            
            var hexThrottleSub = hexObs
                .Throttle(TimeSpan.FromMilliseconds(300))
                .Subscribe(_ => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (!_isUpdating) OnHexChanged();
                    PendingIndicator.IsVisible = false;
                }));

            _disposables.Add(pendingSub);
            _disposables.Add(hexThrottleSub);

            // Initialize to red
            InitializeColor(0, 1, 1, 255); // Hue=0 (red), full saturation, full value, full opacity
        }

        private void InitializeColor(double hue, double saturation, double value, byte alpha)
        {
            _isUpdating = true;
            
            HueSlider.Hue = hue;
            SvPicker.Hue = hue;
            SvPicker.Saturation = saturation;
            SvPicker.Value = value;
            BrightnessSlider.Brightness = value;
            AlphaSlider.Value = alpha;
            
            UpdateRgbFromHsv();
            UpdatePreview();
            UpdateAlphaSliderGradient();
            
            _isUpdating = false;
        }

        private void OnHsvChanged()
        {
            _isUpdating = true;
            
            // Sync hue between HueSlider and SvPicker
            var hue = HueSlider.Hue;
            SvPicker.Hue = hue;
            
            UpdateRgbFromHsv();
            UpdateAlphaSliderGradient();
            UpdatePreview();
            
            _isUpdating = false;
        }

        private void OnBrightnessChanged()
        {
            _isUpdating = true;
            
            // Apply brightness as a multiplier to the HSV value
            SvPicker.Value = BrightnessSlider.Brightness;
            
            UpdateRgbFromHsv();
            UpdateAlphaSliderGradient();
            UpdatePreview();
            
            _isUpdating = false;
        }

        private void OnAlphaChanged()
        {
            _isUpdating = true;
            
            UpdatePreview();
            UpdateHexWithAlpha();
            
            _isUpdating = false;
        }

        private void OnRgbChanged()
        {
            _isUpdating = true;
            
            var r = RedSlider.Value;
            var g = GreenSlider.Value;
            var b = BlueSlider.Value;
            var a = AlphaSlider.Value;
            
            var color = Color.FromArgb(a, r, g, b);
            ColorUtils.ColorToHsv(color, out var h, out var s, out var v);
            
            HueSlider.Hue = h;
            SvPicker.Hue = h;
            SvPicker.Saturation = s;
            SvPicker.Value = v;
            BrightnessSlider.Brightness = v;
            
            UpdateHexWithAlpha();
            UpdateRgbSliderGradients();
            UpdateAlphaSliderGradient();
            UpdatePreview();
            
            _isUpdating = false;
        }

        private void OnHexChanged()
        {
            var hex = HexInput.Text?.Trim();
            if (string.IsNullOrEmpty(hex)) return;
            
            if (hex.StartsWith("#")) hex = hex.Substring(1);
            
            if (hex.Length == 3)
            {
                hex = string.Concat(hex.Select(c => new string(c, 2)));
            }
            
            // Support both #RRGGBB (6 digits) and #AARRGGBB (8 digits)
            byte a = 255; // Default to fully opaque
            byte r = 0, g = 0, b = 0;
            bool parsed = false;
            
            if (hex.Length == 8 &&
                byte.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out a) &&
                byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out r) &&
                byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out g) &&
                byte.TryParse(hex.Substring(6, 2), System.Globalization.NumberStyles.HexNumber, null, out b))
            {
                parsed = true;
            }
            else if (hex.Length == 6 && 
                byte.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out r) &&
                byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out g) &&
                byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out b))
            {
                parsed = true;
            }
            
            if (parsed)
            {
                _isUpdating = true;
                
                RedSlider.Value = r;
                GreenSlider.Value = g;
                BlueSlider.Value = b;
                AlphaSlider.Value = a;
                
                var color = Color.FromArgb(a, r, g, b);
                ColorUtils.ColorToHsv(color, out var h, out var s, out var v);
                
                HueSlider.Hue = h;
                SvPicker.Hue = h;
                SvPicker.Saturation = s;
                SvPicker.Value = v;
                BrightnessSlider.Brightness = v;
                
                UpdateRgbSliderGradients();
                UpdateAlphaSliderGradient();
                UpdatePreview();
                
                _isUpdating = false;
            }
        }

        private void UpdateRgbFromHsv()
        {
            var alpha = AlphaSlider.Value;
            var color = ColorUtils.ColorFromHsv(SvPicker.Hue, SvPicker.Saturation, SvPicker.Value, alpha);
            
            RedSlider.Value = color.R;
            GreenSlider.Value = color.G;
            BlueSlider.Value = color.B;
            
            UpdateHexWithAlpha();
            UpdateRgbSliderGradients();
        }

        private void UpdateHexWithAlpha()
        {
            var r = RedSlider.Value;
            var g = GreenSlider.Value;
            var b = BlueSlider.Value;
            var a = AlphaSlider.Value;
            
            if (a < 255)
            {
                HexInput.Text = $"#{a:X2}{r:X2}{g:X2}{b:X2}";
            }
            else
            {
                HexInput.Text = $"#{r:X2}{g:X2}{b:X2}";
            }
        }

        private void UpdateAlphaSliderGradient()
        {
            var r = RedSlider.Value;
            var g = GreenSlider.Value;
            var b = BlueSlider.Value;
            
            AlphaSlider.StartColor = Color.FromArgb(0, r, g, b);
            AlphaSlider.EndColor = Color.FromArgb(255, r, g, b);
        }

        private void UpdateRgbSliderGradients()
        {
            var r = RedSlider.Value;
            var g = GreenSlider.Value;
            var b = BlueSlider.Value;
            
            // Update Red slider gradient
            RedSlider.StartColor = Color.FromRgb(0, g, b);
            RedSlider.EndColor = Color.FromRgb(255, g, b);
            
            // Update Green slider gradient
            GreenSlider.StartColor = Color.FromRgb(r, 0, b);
            GreenSlider.EndColor = Color.FromRgb(r, 255, b);
            
            // Update Blue slider gradient
            BlueSlider.StartColor = Color.FromRgb(r, g, 0);
            BlueSlider.EndColor = Color.FromRgb(r, g, 255);
        }

        private void UpdatePreview()
        {
            var alpha = AlphaSlider.Value;
            var color = ColorUtils.ColorFromHsv(SvPicker.Hue, SvPicker.Saturation, SvPicker.Value, alpha);
            ColorPreview.Background = new SolidColorBrush(color);
        }

        public (double H, double S, double V) GetHsv() => (SvPicker.Hue, SvPicker.Saturation, SvPicker.Value);
        
        public byte GetAlpha() => AlphaSlider.Value;

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _disposables.Dispose();
        }
    }
}
