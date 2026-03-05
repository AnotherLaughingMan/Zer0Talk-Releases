using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Zer0Talk.Utilities;

namespace Zer0Talk.Controls
{
    // Lightweight reusable flag control: bind CountryCode to render a real flag image asset.
    public class FlagImage : TemplatedControl
    {
        public static readonly StyledProperty<string?> CountryCodeProperty =
            AvaloniaProperty.Register<FlagImage, string?>(nameof(CountryCode));

        public static readonly StyledProperty<double> FlagWidthProperty =
            AvaloniaProperty.Register<FlagImage, double>(nameof(FlagWidth), 20d);

        public static readonly StyledProperty<double> FlagHeightProperty =
            AvaloniaProperty.Register<FlagImage, double>(nameof(FlagHeight), 14d);

        public string? CountryCode
        {
            get => GetValue(CountryCodeProperty);
            set => SetValue(CountryCodeProperty, value);
        }

        public double FlagWidth
        {
            get => GetValue(FlagWidthProperty);
            set => SetValue(FlagWidthProperty, value);
        }

        public double FlagHeight
        {
            get => GetValue(FlagHeightProperty);
            set => SetValue(FlagHeightProperty, value);
        }

        public override void Render(Avalonia.Media.DrawingContext context)
        {
            base.Render(context);

            var image = FlagImageCatalog.TryGetFlagImage(CountryCode);
            if (image is null) return;

            var width = FlagWidth > 0 ? FlagWidth : Bounds.Width;
            var height = FlagHeight > 0 ? FlagHeight : Bounds.Height;
            var rect = new Rect(0, 0, width, height);
            context.DrawImage(image, new Rect(0, 0, image.Size.Width, image.Size.Height), rect);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var w = FlagWidth > 0 ? FlagWidth : 20d;
            var h = FlagHeight > 0 ? FlagHeight : 14d;
            return new Size(w, h);
        }
    }
}
