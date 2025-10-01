using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace ZTalk.Views.Controls;

// Lightweight drawing surface for rolling traffic history.
// Draws three series (TCP, UDP, Outbound) using DrawingContext without allocating visual children each tick.
public sealed class TrafficHistoryView : Control
{
    public static readonly StyledProperty<ObservableCollection<ZTalk.ViewModels.MonitoringViewModel.TrafficSample>?> HistoryProperty =
        AvaloniaProperty.Register<TrafficHistoryView, ObservableCollection<ZTalk.ViewModels.MonitoringViewModel.TrafficSample>?>(nameof(History));

    public ObservableCollection<ZTalk.ViewModels.MonitoringViewModel.TrafficSample>? History
    {
        get => GetValue(HistoryProperty);
        set => SetValue(HistoryProperty, value);
    }

    // Fixed brushes to avoid theme-based flashes; stable, non-animated colors
    private static readonly IBrush s_tcpBrush = new SolidColorBrush(Color.FromUInt32(0xFF1DB954)); // Spotify green-ish (calmer)
    private static readonly IBrush s_udpBrush = new SolidColorBrush(Color.FromUInt32(0xFF1E90FF)); // DodgerBlue
    private static readonly IBrush s_outBrush = new SolidColorBrush(Color.FromUInt32(0xFFAAAAAA)); // Light gray for auxiliary

    // Subtle glow/shadow brushes (low alpha) used behind the main strokes
    private static readonly IBrush s_tcpGlow = new SolidColorBrush(Color.FromUInt32(0x4028C76F)); // ~25% alpha
    private static readonly IBrush s_udpGlow = new SolidColorBrush(Color.FromUInt32(0x402196F3));
    private static readonly IBrush s_outGlow = new SolidColorBrush(Color.FromUInt32(0x40555555));

    // Grid + axes styling (Task Manager-like: light, unobtrusive)
    private static readonly Pen s_gridPen = new(new SolidColorBrush(Color.FromUInt32(0x20333333)), 1); // ~12% alpha dark line
    private static readonly Pen s_borderPen = new(new SolidColorBrush(Color.FromUInt32(0xFF3A3A3A)), 1);

    // Axis text: monospaced preference with fallbacks
    private static readonly Typeface s_axisTypeface = new("Consolas");
    private static readonly Typeface s_axisTypefaceFallback = new("Courier New");

    private INotifyCollectionChanged? _subscribed;

    public TrafficHistoryView() { }

    protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HistoryProperty)
        {
            Resubscribe();
        }
    }

    private void Resubscribe()
    {
        if (_subscribed is not null)
        {
            _subscribed.CollectionChanged -= OnHistoryChanged;
            _subscribed = null;
        }
        if (History is not null)
        {
            _subscribed = History;
            _subscribed.CollectionChanged += OnHistoryChanged;
        }
        InvalidateVisual();
    }

    private void OnHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateVisual();
    }

    // Track last unit label to emit trace entries when scaling changes (for monitoring.log + error.txt)
    private string _lastUnit = string.Empty;
    private static bool _styleInitLogged;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var hist = History;
        var bounds = Bounds;
        if (bounds.Width <= 2 || bounds.Height <= 2)
            return;

        // Background remains transparent; container Border supplies neutral surface color
        context.FillRectangle(Brushes.Transparent, bounds);

        if (hist is null || hist.Count == 0)
            return; // Persistent background, no lines yet

        // Determine visible range (last N points to fit width). Keep ~1px per sample.
        var maxPoints = Math.Max(10, (int)Math.Floor(bounds.Width));
        var start = Math.Max(0, hist.Count - maxPoints);

        // Compute max Y from visible samples (bytes/sec), convert to bits/sec for display like Task Manager
        double maxYBytes = 1;
        for (int i = start; i < hist.Count; i++)
        {
            var s = hist[i];
            if (s.Tcp > maxYBytes) maxYBytes = s.Tcp;
            if (s.Udp > maxYBytes) maxYBytes = s.Udp;
            if (s.Out > maxYBytes) maxYBytes = s.Out;
        }
        if (maxYBytes <= 0) maxYBytes = 1; // avoid divide by zero

        // Scale to bits/sec for axes; choose human unit (Kbps/Mbps)
        double maxBps = maxYBytes * 8.0;
        var (unit, unitDiv) = ChooseUnit(maxBps); // returns ("Kbps", 1_000) or ("Mbps", 1_000_000) etc.
        double scaledMax = Math.Max(1, maxBps / unitDiv);

        // Inner chart rect (padding for axes)
        var left = bounds.X + 36; // leave space for Y labels
        var top = bounds.Y + 8;
        var width = Math.Max(1, bounds.Width - 48);
        var height = Math.Max(1, bounds.Height - 28); // leave space for X labels
        var baselineY = top + height; // origin bottom-left

        // Draw grid: 5 vertical divisions (time), 4 horizontal lines (25%, 50%, 75%, 100%)
        DrawGrid(context, left, top, width, height);

        // Build geometries for three series
        var tcpGeo = new StreamGeometry();
        var udpGeo = new StreamGeometry();
        var outGeo = new StreamGeometry();

        using (var ctx = tcpGeo.Open())
        using (var ctx2 = udpGeo.Open())
        using (var ctx3 = outGeo.Open())
        {
            int visibleCount = hist.Count - start;
            double stepX = visibleCount > 1 ? width / (visibleCount - 1) : width;
            for (int i = 0; i < visibleCount; i++)
            {
                var s = hist[start + i];
                double x = left + i * stepX;
                double yTcp = baselineY - Math.Min(1.0, ((s.Tcp * 8.0) / unitDiv) / scaledMax) * height;
                double yUdp = baselineY - Math.Min(1.0, ((s.Udp * 8.0) / unitDiv) / scaledMax) * height;
                double yOut = baselineY - Math.Min(1.0, ((s.Out * 8.0) / unitDiv) / scaledMax) * height;
                if (i == 0)
                {
                    ctx.BeginFigure(new Point(x, yTcp), false);
                    ctx2.BeginFigure(new Point(x, yUdp), false);
                    ctx3.BeginFigure(new Point(x, yOut), false);
                }
                else
                {
                    ctx.LineTo(new Point(x, yTcp));
                    ctx2.LineTo(new Point(x, yUdp));
                    ctx3.LineTo(new Point(x, yOut));
                }
            }
            ctx.EndFigure(false);
            ctx2.EndFigure(false);
            ctx3.EndFigure(false);
        }

        // Draw subtle glow behind each series (wider, low alpha), then the crisp line
        context.DrawGeometry(null, new Pen(s_tcpGlow, 3), tcpGeo);
        context.DrawGeometry(null, new Pen(s_udpGlow, 3), udpGeo);
        context.DrawGeometry(null, new Pen(s_outGlow, 2.5, dashStyle: DashStyle.Dash), outGeo);

        var tcpPen = new Pen(s_tcpBrush, 1.3);
        var udpPen = new Pen(s_udpBrush, 1.3);
        var outPen = new Pen(s_outBrush, 1.1, dashStyle: DashStyle.Dash);
        context.DrawGeometry(null, tcpPen, tcpGeo);
        context.DrawGeometry(null, udpPen, udpGeo);
        context.DrawGeometry(null, outPen, outGeo);

        // Border framing
        context.DrawRectangle(null, s_borderPen, new Rect(left, top, width, height));

        // Axis labels: Y (right side) and X (bottom). Monospaced small font.
        DrawYAxisLabels(context, left, top, width, height, scaledMax, unit);
        DrawXAxisLabels(context, left, top, width, height);

        // Log style initialization and unit changes
        if (!_styleInitLogged)
        {
            LogStyle("Monitoring graph style applied (grid, axes, glow lines)");
            _styleInitLogged = true;
        }
        if (!string.Equals(_lastUnit, unit, StringComparison.Ordinal))
        {
            LogStyle($"Monitoring graph autoscale unit -> {unit}");
            _lastUnit = unit;
        }
    }

    private static (string Unit, double Div) ChooseUnit(double bps)
    {
        // Prefer Kbps/Mbps/Gbps (decimal) like Task Manager
        if (bps >= 1_000_000_000) return ("Gbps", 1_000_000_000.0);
        if (bps >= 1_000_000) return ("Mbps", 1_000_000.0);
        if (bps >= 1_000) return ("Kbps", 1_000.0);
        return ("bps", 1.0);
    }

    private static void DrawGrid(DrawingContext ctx, double left, double top, double width, double height)
    {
        // Vertical: 5 divisions
        int vDiv = 5;
        for (int i = 0; i <= vDiv; i++)
        {
            double x = left + (width * i / vDiv);
            ctx.DrawLine(s_gridPen, new Point(x, top), new Point(x, top + height));
        }
        // Horizontal: 4 lines at 25%, 50%, 75%, 100%
        int hDiv = 4;
        for (int i = 1; i <= hDiv; i++)
        {
            double y = top + (height * (hDiv - i + 1) / (hDiv + 1)); // 25%, 50%, 75%, then top border handles 100%
            ctx.DrawLine(s_gridPen, new Point(left, y), new Point(left + width, y));
        }
    }

    private static void DrawYAxisLabels(DrawingContext ctx, double left, double top, double width, double height, double maxScaled, string unit)
    {
        // Labels at 25/50/75/100% on the left gutter area with monospaced font
        void DrawAt(double frac)
        {
            double val = maxScaled * frac;
            string text = FormatNumber(val) + " " + unit;
            double y = top + (1.0 - frac) * height - 6; // rough vertical center adjustment
            double x = Math.Max(0, left - 34); // fixed-width gutter for labels
            var layout = new TextLayout(text, s_axisTypeface, 10, foreground: Brushes.Gray);
            layout.Draw(ctx, new Point(x, y));
        }
        DrawAt(1.0);
        DrawAt(0.75);
        DrawAt(0.5);
        DrawAt(0.25);
    }

    private static void DrawXAxisLabels(DrawingContext ctx, double left, double top, double width, double height)
    {
        // 5-minute visual mimic: show ticks at -5m, -4m, -3m, -2m, -1m, Now
        // Note: exact mapping depends on sample interval; this is a visual aid only.
        var labels = new[] { "-5m", "-4m", "-3m", "-2m", "-1m", "Now" };
        for (int i = 0; i < labels.Length; i++)
        {
            double frac = i / (labels.Length - 1.0);
            double x = left + frac * width - 10; // approximate centering
            double y = top + height + 6;
            var layout = new TextLayout(labels[i], s_axisTypeface, 10, foreground: Brushes.Gray);
            layout.Draw(ctx, new Point(x, y));
        }
    }

    private static string FormatNumber(double v)
    {
        if (v >= 1000) return v.ToString("0.#", CultureInfo.InvariantCulture);
        if (v >= 100) return v.ToString("0", CultureInfo.InvariantCulture);
        return v.ToString("0.#", CultureInfo.InvariantCulture);
    }

    private static void LogStyle(string message)
    {
        try
        {
            if (ZTalk.Utilities.LoggingPaths.Enabled)
            {
                var line = $"[STYLE] {DateTime.Now:O} {message}";
                File.AppendAllText(ZTalk.Utilities.LoggingPaths.Monitoring, line + Environment.NewLine);
            }
        }
        catch { }
        try
        {
            // Mirror to error.txt for traceability as requested
            ZTalk.Utilities.ErrorLogger.LogException(new InvalidOperationException(message), source: "MonitoringStyle");
        }
        catch { }
    }
}
