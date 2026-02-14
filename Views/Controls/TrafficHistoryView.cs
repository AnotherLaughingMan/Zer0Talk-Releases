using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Zer0Talk.Views.Controls;

// Lightweight drawing surface for rolling traffic history.
// Draws three series (TCP, UDP, Outbound) using DrawingContext without allocating visual children each tick.
public sealed class TrafficHistoryView : Control
{
    public static readonly StyledProperty<ObservableCollection<Zer0Talk.ViewModels.MonitoringViewModel.TrafficSample>?> HistoryProperty =
        AvaloniaProperty.Register<TrafficHistoryView, ObservableCollection<Zer0Talk.ViewModels.MonitoringViewModel.TrafficSample>?>(nameof(History));
    public static readonly StyledProperty<int> GraphStyleIndexProperty =
        AvaloniaProperty.Register<TrafficHistoryView, int>(nameof(GraphStyleIndex), 0);
    public static readonly StyledProperty<int> LegendPositionIndexProperty =
        AvaloniaProperty.Register<TrafficHistoryView, int>(nameof(LegendPositionIndex), 1);
    public static readonly StyledProperty<int> LegendSideProperty =
        AvaloniaProperty.Register<TrafficHistoryView, int>(nameof(LegendSide), 1);
    public static readonly StyledProperty<bool> EnableRealtimeSmoothingProperty =
        AvaloniaProperty.Register<TrafficHistoryView, bool>(nameof(EnableRealtimeSmoothing), false);

    public ObservableCollection<Zer0Talk.ViewModels.MonitoringViewModel.TrafficSample>? History
    {
        get => GetValue(HistoryProperty);
        set => SetValue(HistoryProperty, value);
    }

    public int GraphStyleIndex
    {
        get => Math.Clamp(GetValue(GraphStyleIndexProperty), 0, 2);
        set => SetValue(GraphStyleIndexProperty, Math.Clamp(value, 0, 2));
    }

    public bool EnableRealtimeSmoothing
    {
        get => GetValue(EnableRealtimeSmoothingProperty);
        set => SetValue(EnableRealtimeSmoothingProperty, value);
    }

    public int LegendPositionIndex
    {
        get => Math.Clamp(GetValue(LegendPositionIndexProperty), 0, 1);
        set => SetValue(LegendPositionIndexProperty, Math.Clamp(value, 0, 1));
    }

    public int LegendSide
    {
        get => Math.Clamp(GetValue(LegendSideProperty), 0, 1);
        set => SetValue(LegendSideProperty, Math.Clamp(value, 0, 1));
    }

    // Fixed brushes to avoid theme-based flashes; stable, non-animated colors
    private static readonly IBrush s_tcpBrush = new SolidColorBrush(Color.FromUInt32(0xFF1DB954)); // Spotify green-ish (calmer)
    private static readonly IBrush s_udpBrush = new SolidColorBrush(Color.FromUInt32(0xFF1E90FF)); // DodgerBlue
    private static readonly IBrush s_outBrush = new SolidColorBrush(Color.FromUInt32(0xFFAAAAAA)); // Light gray for auxiliary
    private static readonly IBrush s_recvBrush = new SolidColorBrush(Color.FromUInt32(0xFFBF7BFF)); // Light purple for received

    // Subtle glow/shadow brushes (low alpha) used behind the main strokes
    private static readonly IBrush s_tcpGlow = new SolidColorBrush(Color.FromUInt32(0x4028C76F)); // ~25% alpha
    private static readonly IBrush s_udpGlow = new SolidColorBrush(Color.FromUInt32(0x402196F3));
    private static readonly IBrush s_outGlow = new SolidColorBrush(Color.FromUInt32(0x40555555));
    private static readonly IBrush s_udpContrastGlow = new SolidColorBrush(Color.FromUInt32(0x55BFE7FF));
    private static readonly IBrush s_recvGlow = new SolidColorBrush(Color.FromUInt32(0x55D8B8FF));

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
        if (change.Property == GraphStyleIndexProperty || change.Property == EnableRealtimeSmoothingProperty || change.Property == LegendPositionIndexProperty || change.Property == LegendSideProperty)
        {
            if (change.Property == EnableRealtimeSmoothingProperty && !EnableRealtimeSmoothing)
            {
                _smoothedScaleCeilingBps = 0;
            }
            if (change.Property == LegendSideProperty)
            {
                LegendPositionIndex = LegendSide;
            }
            InvalidateVisual();
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
    private double _smoothedScaleCeilingBps;

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
            if (s.Recv > maxYBytes) maxYBytes = s.Recv;
        }
        if (maxYBytes <= 0) maxYBytes = 1; // avoid divide by zero

        // Scale to bits/sec for axes; add headroom/rounded ceiling and smooth decay to reduce jitter.
        double maxBps = maxYBytes * 8.0;
        var targetCeilingBps = ComputeScaleCeiling(maxBps);
        maxBps = EnableRealtimeSmoothing ? SmoothScaleCeiling(targetCeilingBps) : targetCeilingBps;
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

        var style = GraphStyleIndex;
        if (style == 1)
        {
            DrawBarSeries(context, hist, start, left, width, baselineY, height, unitDiv, scaledMax);
        }
        else if (style == 2)
        {
            DrawShadedSeries(context, hist, start, left, width, baselineY, height, unitDiv, scaledMax);
        }
        else
        {
            DrawLineSeries(context, hist, start, left, width, baselineY, height, unitDiv, scaledMax, solid: false);
        }

        // Border framing
        context.DrawRectangle(null, s_borderPen, new Rect(left, top, width, height));

        // Built-in series legend for chart colors/styles.
        DrawSeriesLegend(context, left, top, width, LegendPositionIndex);

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

    private static double ComputeScaleCeiling(double maxBps)
    {
        var withHeadroom = Math.Max(1.0, maxBps * 1.15);
        var exponent = Math.Floor(Math.Log10(withHeadroom));
        var magnitude = Math.Pow(10, exponent);
        var normalized = withHeadroom / magnitude;
        var nice = normalized <= 1.0 ? 1.0
            : normalized <= 2.0 ? 2.0
            : normalized <= 5.0 ? 5.0
            : 10.0;
        return nice * magnitude;
    }

    private static void DrawLineSeries(
        DrawingContext context,
        ObservableCollection<Zer0Talk.ViewModels.MonitoringViewModel.TrafficSample> hist,
        int start,
        double left,
        double width,
        double baselineY,
        double height,
        double unitDiv,
        double scaledMax,
        bool solid)
    {
        var tcpGeo = new StreamGeometry();
        var udpGeo = new StreamGeometry();
        var outGeo = new StreamGeometry();
        var recvGeo = new StreamGeometry();

        using (var ctx = tcpGeo.Open())
        using (var ctx2 = udpGeo.Open())
        using (var ctx3 = outGeo.Open())
        using (var ctx4 = recvGeo.Open())
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
                double yRecv = baselineY - Math.Min(1.0, ((s.Recv * 8.0) / unitDiv) / scaledMax) * height;
                if (i == 0)
                {
                    ctx.BeginFigure(new Point(x, yTcp), false);
                    ctx2.BeginFigure(new Point(x, yUdp), false);
                    ctx3.BeginFigure(new Point(x, yOut), false);
                    ctx4.BeginFigure(new Point(x, yRecv), false);
                }
                else
                {
                    ctx.LineTo(new Point(x, yTcp));
                    ctx2.LineTo(new Point(x, yUdp));
                    ctx3.LineTo(new Point(x, yOut));
                    ctx4.LineTo(new Point(x, yRecv));
                }
            }
            ctx.EndFigure(false);
            ctx2.EndFigure(false);
            ctx3.EndFigure(false);
            ctx4.EndFigure(false);
        }

        if (!solid)
        {
            context.DrawGeometry(null, new Pen(s_tcpGlow, 3), tcpGeo);
            context.DrawGeometry(null, new Pen(s_udpGlow, 3), udpGeo);
            context.DrawGeometry(null, new Pen(s_udpContrastGlow, 2.1), udpGeo);
            context.DrawGeometry(null, new Pen(s_outGlow, 2.5, dashStyle: DashStyle.Dash), outGeo);
            context.DrawGeometry(null, new Pen(s_recvGlow, 2.2), recvGeo);
        }

        var tcpPen = new Pen(s_tcpBrush, solid ? 2.1 : 1.3);
        var udpPen = new Pen(s_udpBrush, solid ? 2.1 : 1.8, dashStyle: DashStyle.Dash);
        var outPen = new Pen(s_outBrush, solid ? 2.1 : 1.1, dashStyle: solid ? null : DashStyle.Dash);
        context.DrawGeometry(null, tcpPen, tcpGeo);
        context.DrawGeometry(null, udpPen, udpGeo);
        context.DrawGeometry(null, outPen, outGeo);
        context.DrawGeometry(null, new Pen(s_recvBrush, solid ? 2.1 : 1.8), recvGeo);
    }

    private static void DrawShadedSeries(
        DrawingContext context,
        ObservableCollection<Zer0Talk.ViewModels.MonitoringViewModel.TrafficSample> hist,
        int start,
        double left,
        double width,
        double baselineY,
        double height,
        double unitDiv,
        double scaledMax)
    {
        var tcpLine = new StreamGeometry();
        var udpLine = new StreamGeometry();
        var outLine = new StreamGeometry();
        var recvLine = new StreamGeometry();
        var tcpArea = new StreamGeometry();
        var udpArea = new StreamGeometry();
        var outArea = new StreamGeometry();
        var recvArea = new StreamGeometry();

        int visibleCount = hist.Count - start;
        if (visibleCount <= 0) return;
        double stepX = visibleCount > 1 ? width / (visibleCount - 1) : width;

        var pointsTcp = new Point[visibleCount];
        var pointsUdp = new Point[visibleCount];
        var pointsOut = new Point[visibleCount];
        var pointsRecv = new Point[visibleCount];
        for (int i = 0; i < visibleCount; i++)
        {
            var s = hist[start + i];
            double x = left + i * stepX;
            double yTcp = baselineY - Math.Min(1.0, ((s.Tcp * 8.0) / unitDiv) / scaledMax) * height;
            double yUdp = baselineY - Math.Min(1.0, ((s.Udp * 8.0) / unitDiv) / scaledMax) * height;
            double yOut = baselineY - Math.Min(1.0, ((s.Out * 8.0) / unitDiv) / scaledMax) * height;
            double yRecv = baselineY - Math.Min(1.0, ((s.Recv * 8.0) / unitDiv) / scaledMax) * height;
            pointsTcp[i] = new Point(x, yTcp);
            pointsUdp[i] = new Point(x, yUdp);
            pointsOut[i] = new Point(x, yOut);
            pointsRecv[i] = new Point(x, yRecv);
        }

        using (var line = tcpLine.Open())
        {
            line.BeginFigure(pointsTcp[0], false);
            for (int i = 1; i < pointsTcp.Length; i++) line.LineTo(pointsTcp[i]);
            line.EndFigure(false);
        }
        using (var line = udpLine.Open())
        {
            line.BeginFigure(pointsUdp[0], false);
            for (int i = 1; i < pointsUdp.Length; i++) line.LineTo(pointsUdp[i]);
            line.EndFigure(false);
        }
        using (var line = outLine.Open())
        {
            line.BeginFigure(pointsOut[0], false);
            for (int i = 1; i < pointsOut.Length; i++) line.LineTo(pointsOut[i]);
            line.EndFigure(false);
        }
        using (var line = recvLine.Open())
        {
            line.BeginFigure(pointsRecv[0], false);
            for (int i = 1; i < pointsRecv.Length; i++) line.LineTo(pointsRecv[i]);
            line.EndFigure(false);
        }

        BuildAreaGeometry(tcpArea, pointsTcp, baselineY);
        BuildAreaGeometry(udpArea, pointsUdp, baselineY);
        BuildAreaGeometry(outArea, pointsOut, baselineY);
        BuildAreaGeometry(recvArea, pointsRecv, baselineY);

        var tcpFill = new SolidColorBrush(Color.FromUInt32(0x3A1DB954));
        var udpFill = new SolidColorBrush(Color.FromUInt32(0x551E90FF));
        var outFill = new SolidColorBrush(Color.FromUInt32(0x26AAAAAA));
        var recvFill = new SolidColorBrush(Color.FromUInt32(0x50BF7BFF));

        // Draw order matters for overlap visibility: draw OUT first, then TCP, then UDP on top.
        context.DrawGeometry(outFill, null, outArea);
        context.DrawGeometry(tcpFill, null, tcpArea);
        context.DrawGeometry(udpFill, null, udpArea);
        context.DrawGeometry(recvFill, null, recvArea);

        context.DrawGeometry(null, new Pen(s_outBrush, 1.2), outLine);
        context.DrawGeometry(null, new Pen(s_tcpBrush, 1.6), tcpLine);
        context.DrawGeometry(null, new Pen(s_udpContrastGlow, 2.4), udpLine);
        context.DrawGeometry(null, new Pen(s_udpBrush, 1.9, dashStyle: DashStyle.Dash), udpLine);
        context.DrawGeometry(null, new Pen(s_recvGlow, 2.4), recvLine);
        context.DrawGeometry(null, new Pen(s_recvBrush, 1.9), recvLine);
    }

    private static void BuildAreaGeometry(StreamGeometry geometry, Point[] points, double baselineY)
    {
        if (points.Length == 0) return;
        using var area = geometry.Open();
        area.BeginFigure(new Point(points[0].X, baselineY), true);
        area.LineTo(points[0]);
        for (int i = 1; i < points.Length; i++) area.LineTo(points[i]);
        area.LineTo(new Point(points[^1].X, baselineY));
        area.EndFigure(true);
    }

    private static void DrawBarSeries(
        DrawingContext context,
        ObservableCollection<Zer0Talk.ViewModels.MonitoringViewModel.TrafficSample> hist,
        int start,
        double left,
        double width,
        double baselineY,
        double height,
        double unitDiv,
        double scaledMax)
    {
        int visibleCount = hist.Count - start;
        if (visibleCount <= 0) return;

        // Bucket samples so 3 bars (TCP/UDP/OUT) remain distinct even at dense realtime widths.
        const double minPixelsPerGroup = 5.0;
        var maxGroupsByWidth = Math.Max(1, (int)Math.Floor(width / minPixelsPerGroup));
        var groups = Math.Min(visibleCount, maxGroupsByWidth);
        var stride = Math.Max(1, (int)Math.Ceiling((double)visibleCount / groups));
        var stepX = groups > 1 ? width / (groups - 1) : width;
        var groupWidth = Math.Max(4.0, stepX * 0.86);
        var singleBar = Math.Max(1.0, Math.Floor((groupWidth - 3) / 4.0));

        for (int g = 0; g < groups; g++)
        {
            var from = start + (g * stride);
            var to = Math.Min(start + visibleCount, from + stride);
            double tcp = 0, udp = 0, outbound = 0, recv = 0;
            for (int i = from; i < to; i++)
            {
                var s = hist[i];
                if (s.Tcp > tcp) tcp = s.Tcp;
                if (s.Udp > udp) udp = s.Udp;
                if (s.Out > outbound) outbound = s.Out;
                if (s.Recv > recv) recv = s.Recv;
            }

            var centerX = left + (g * stepX);
            var baseX = centerX - ((singleBar * 4 + 3) / 2.0);

            DrawBar(context, s_tcpBrush, baseX, singleBar, tcp, baselineY, height, unitDiv, scaledMax);
            DrawBar(context, s_udpBrush, baseX + singleBar + 1, singleBar, udp, baselineY, height, unitDiv, scaledMax);
            DrawBar(context, s_outBrush, baseX + (singleBar + 1) * 2, singleBar, outbound, baselineY, height, unitDiv, scaledMax);
            DrawBar(context, s_recvBrush, baseX + (singleBar + 1) * 3, singleBar, recv, baselineY, height, unitDiv, scaledMax);
        }
    }

    private static void DrawBar(DrawingContext context, IBrush brush, double x, double width, double valueBytesPerSec, double baselineY, double chartHeight, double unitDiv, double scaledMax)
    {
        var norm = Math.Min(1.0, ((valueBytesPerSec * 8.0) / unitDiv) / scaledMax);
        var h = norm * chartHeight;
        if (h <= 0.5) return;
        var y = baselineY - h;
        context.FillRectangle(brush, new Rect(x, y, width, h));
    }

    private static void DrawSeriesLegend(DrawingContext context, double left, double top, double width, int legendPositionIndex)
    {
        var legendWidth = 224.0;
        var legendX = legendPositionIndex == 0
            ? left + 6
            : left + Math.Max(6, width - legendWidth - 6);
        var legendY = top + 6;
        var legendBg = new SolidColorBrush(Color.FromUInt32(0x88202020));
        var legendBorder = new Pen(new SolidColorBrush(Color.FromUInt32(0x664A4A4A)), 1);
        context.DrawRectangle(legendBg, legendBorder, new Rect(legendX, legendY, legendWidth, 50), 5, 5);

        DrawLegendItem(context, legendX + 8, legendY + 8, s_tcpBrush, "TCP", dashed: false);
        DrawLegendItem(context, legendX + 62, legendY + 8, s_udpBrush, "UDP", dashed: true);
        DrawLegendItem(context, legendX + 116, legendY + 8, s_outBrush, "OUT", dashed: true);
        DrawLegendItem(context, legendX + 170, legendY + 8, s_recvBrush, "RECV", dashed: false);
    }

    private static void DrawLegendItem(DrawingContext context, double x, double y, IBrush brush, string label, bool dashed)
    {
        var pen = new Pen(brush, 2, dashStyle: dashed ? DashStyle.Dash : null);
        context.DrawLine(pen, new Point(x, y + 5), new Point(x + 18, y + 5));
        var text = new TextLayout(label, s_axisTypeface, 10, foreground: Brushes.Gainsboro);
        text.Draw(context, new Point(x + 22, y - 2));
    }

    private double SmoothScaleCeiling(double targetCeilingBps)
    {
        if (_smoothedScaleCeilingBps <= 0)
        {
            _smoothedScaleCeilingBps = targetCeilingBps;
            return _smoothedScaleCeilingBps;
        }

        // Rise quickly so bursts are visible immediately; decay slowly to avoid y-axis jitter.
        if (targetCeilingBps > _smoothedScaleCeilingBps)
        {
            _smoothedScaleCeilingBps = targetCeilingBps;
        }
        else
        {
            const double decay = 0.12;
            _smoothedScaleCeilingBps = _smoothedScaleCeilingBps + ((targetCeilingBps - _smoothedScaleCeilingBps) * decay);
            if (_smoothedScaleCeilingBps < targetCeilingBps)
            {
                _smoothedScaleCeilingBps = targetCeilingBps;
            }
        }

        return _smoothedScaleCeilingBps;
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
            if (Zer0Talk.Utilities.LoggingPaths.Enabled)
            {
                var line = $"[STYLE] {DateTime.Now:O} {message}";
                File.AppendAllText(Zer0Talk.Utilities.LoggingPaths.Monitoring, line + Environment.NewLine);
            }
        }
        catch { }
        try
        {
            // Mirror to error.txt for traceability as requested
            Zer0Talk.Utilities.ErrorLogger.LogException(new InvalidOperationException(message), source: "MonitoringStyle");
        }
        catch { }
    }
}
