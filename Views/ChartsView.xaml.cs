using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ProcessMonitor.Models;
using ProcessMonitor.ViewModels;

namespace ProcessMonitor.Views;

public partial class ChartsView : UserControl
{
    private const int MaxHistory = MainViewModel.MaxChartHistory;
    private const double LeftPad = 50;

    private static readonly Brush CpuLine     = Freeze(Color.FromRgb(0,   200, 83));
    private static readonly Brush CpuFill     = Freeze(Color.FromArgb(35,  0,  200, 83));
    private static readonly Brush MemLine     = Freeze(Color.FromRgb(41,  121, 255));
    private static readonly Brush MemFill     = Freeze(Color.FromArgb(35,  41, 121, 255));
    private static readonly Brush DiskReadLine = Freeze(Color.FromRgb(255, 193,   7));
    private static readonly Brush DiskReadFill = Freeze(Color.FromArgb(35, 255, 193,   7));
    private static readonly Brush DiskWriteLine = Freeze(Color.FromRgb(233,  30,  99));
    private static readonly Brush NetRecvLine = Freeze(Color.FromRgb(0,   188, 212));
    private static readonly Brush NetRecvFill = Freeze(Color.FromArgb(35,  0,  188, 212));
    private static readonly Brush NetSendLine = Freeze(Color.FromRgb(255, 152,   0));
    private static readonly Brush GpuLine     = Freeze(Color.FromRgb(156,  39, 176));
    private static readonly Brush GpuFill     = Freeze(Color.FromArgb(35, 156,  39, 176));
    private static readonly Brush GridLines   = Freeze(Color.FromArgb(45, 128, 128, 128));
    private static readonly Brush Labels      = Freeze(Color.FromRgb(130, 130, 130));
    private static readonly FontFamily Mono   = new("Consolas");

    private static Brush Freeze(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    private MainViewModel? _vm;

    public ChartsView()
    {
        InitializeComponent();
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm = DataContext as MainViewModel;
        if (_vm is null) return;
        _vm.ChartHistory.CollectionChanged += OnHistoryChanged;
        Redraw();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_vm is not null)
            _vm.ChartHistory.CollectionChanged -= OnHistoryChanged;
    }

    private void OnHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e) => Redraw();
    private void Canvas_SizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        if (_vm is null) return;
        var h = _vm.ChartHistory;
        var last = h.Count > 0 ? h[^1] : null;

        // CPU
        double cpu = last?.CpuPercent ?? 0;
        double ghz = last?.CpuGhz ?? 0;
        float? cpuTemp = last?.CpuTempC;
        CpuCurrentLabel.Text = $"— {cpu:F1}%";
        CpuGhzLabel.Text     = ghz > 0 ? $"@ {ghz:F2} GHz" : "";
        CpuTempLabel.Text    = cpuTemp.HasValue ? $"| {cpuTemp.Value:F0}°C" : "";
        DrawCoreBar(CoreCanvas, _vm.CurrentCoreUsages);
        ClearAndDrawGrid(CpuCanvas, 100, v => $"{v:F0}%");
        DrawSeries(CpuCanvas, h, p => p.CpuPercent, 100, CpuLine, CpuFill);

        // Memory
        long mem = last?.MemoryMB ?? 0;
        long totalRamMB = (long)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024L * 1024));
        double memPct = totalRamMB > 0 ? (double)mem / totalRamMB * 100.0 : 0;
        string memStr = mem >= 1024 ? $"{mem / 1024.0:F1} GB" : $"{mem:N0} MB";
        MemCurrentLabel.Text = $"— {memStr}  ({memPct:F1}%)";
        MemRamInfoLabel.Text = _vm?.RamLabel ?? "";
        double memMax = h.Count > 0 ? Math.Max(h.Max(p => (double)p.MemoryMB) * 1.15, 512) : 1024;
        ClearAndDrawGrid(MemCanvas, memMax, v => FormatMem(v));
        DrawSeries(MemCanvas, h, p => p.MemoryMB, memMax, MemLine, MemFill);

        // Disk
        long diskRead  = last?.DiskReadBps  ?? 0;
        long diskWrite = last?.DiskWriteBps ?? 0;
        DiskCurrentLabel.Text = $"R {FormatBps(diskRead)}  W {FormatBps(diskWrite)}";
        double diskMax = h.Count > 0
            ? Math.Max(h.Max(p => Math.Max(p.DiskReadBps, p.DiskWriteBps)) * 1.2, 1024 * 512)
            : 1024 * 1024;
        ClearAndDrawGrid(DiskCanvas, diskMax, v => FormatBps((long)v));
        DrawSeries(DiskCanvas, h, p => p.DiskReadBps,  diskMax, DiskReadLine,  DiskReadFill);
        DrawSeries(DiskCanvas, h, p => p.DiskWriteBps, diskMax, DiskWriteLine, null);

        // Network
        long sendBps = last?.NetworkSendBps ?? 0;
        long recvBps = last?.NetworkRecvBps ?? 0;
        NetCurrentLabel.Text = $"↑ {FormatBps(sendBps)}  ↓ {FormatBps(recvBps)}";
        double netMax = h.Count > 0
            ? Math.Max(h.Max(p => Math.Max(p.NetworkSendBps, p.NetworkRecvBps)) * 1.2, 1024 * 100)
            : 1024 * 1024;
        ClearAndDrawGrid(NetCanvas, netMax, v => FormatBps((long)v));
        DrawSeries(NetCanvas, h, p => p.NetworkRecvBps, netMax, NetRecvLine, NetRecvFill);
        DrawSeries(NetCanvas, h, p => p.NetworkSendBps, netMax, NetSendLine, null);

        // GPU
        double gpu = last?.GpuPercent ?? 0;
        float? gpuTemp = last?.GpuTempC;
        GpuCurrentLabel.Text = $"— {gpu:F1}%";
        GpuTempLabel.Text    = gpuTemp.HasValue ? $"| {gpuTemp.Value:F0}°C" : "";
        ClearAndDrawGrid(GpuCanvas, 100, v => $"{v:F0}%");
        DrawSeries(GpuCanvas, h, p => p.GpuPercent, 100, GpuLine, GpuFill);
    }

    private void DrawCoreBar(Canvas canvas, double[] coreUsages)
    {
        canvas.Children.Clear();
        if (coreUsages.Length == 0 || canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0) return;

        double totalW = canvas.ActualWidth;
        double h = canvas.ActualHeight;
        int count = coreUsages.Length;
        double gap = 2;
        double barW = Math.Max(4, (totalW - gap * (count - 1)) / count);

        for (int i = 0; i < count; i++)
        {
            double x = i * (barW + gap);
            double pct = Math.Clamp(coreUsages[i] / 100.0, 0, 1);
            double fillH = pct * h;

            // Background
            canvas.Children.Add(new Rectangle
            {
                Width = barW, Height = h,
                Fill = new SolidColorBrush(Color.FromArgb(40, 0, 200, 83))
            });
            Canvas.SetLeft(canvas.Children[^1], x);
            Canvas.SetTop(canvas.Children[^1], 0);

            // Fill
            if (fillH >= 1)
            {
                canvas.Children.Add(new Rectangle
                {
                    Width = barW, Height = fillH,
                    Fill = CpuLine
                });
                Canvas.SetLeft(canvas.Children[^1], x);
                Canvas.SetTop(canvas.Children[^1], h - fillH);
            }
        }
    }

    private void ClearAndDrawGrid(Canvas canvas, double maxValue, Func<double, string> labelFormat)
    {
        canvas.Children.Clear();
        double w = canvas.ActualWidth;
        double h = canvas.ActualHeight;
        if (w <= 0 || h <= 0 || maxValue <= 0) return;

        foreach (var frac in new[] { 1.0, 0.75, 0.5, 0.25, 0.0 })
        {
            double y = h * (1.0 - frac);

            if (frac > 0.0 && frac < 1.0)
            {
                canvas.Children.Add(new Line
                {
                    X1 = LeftPad, Y1 = y, X2 = w, Y2 = y,
                    Stroke = GridLines, StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 4, 4 }
                });
            }

            var label = new TextBlock
            {
                Text = labelFormat(frac * maxValue),
                Foreground = Labels, FontSize = 10, FontFamily = Mono,
                Width = LeftPad - 4, TextAlignment = TextAlignment.Right
            };
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, Math.Clamp(y - 7, 0, h - 14));
            canvas.Children.Add(label);
        }
    }

    private void DrawSeries(Canvas canvas, IReadOnlyList<ChartDataPoint> history,
        Func<ChartDataPoint, double> getValue, double maxValue,
        Brush lineBrush, Brush? fillBrush)
    {
        if (history.Count < 2) return;
        double w = canvas.ActualWidth;
        double h = canvas.ActualHeight;
        double chartW = w - LeftPad;

        var linePoints = new PointCollection(history.Count);
        PointCollection? fillPoints = fillBrush is not null
            ? new PointCollection(history.Count + 2)
            : null;

        fillPoints?.Add(new Point(LeftPad, h));

        for (int i = 0; i < history.Count; i++)
        {
            double x = LeftPad + (double)i / (MaxHistory - 1) * chartW;
            double val = Math.Clamp(getValue(history[i]), 0, maxValue);
            double y = Math.Clamp(h - val / maxValue * h, 0, h);
            linePoints.Add(new Point(x, y));
            fillPoints?.Add(new Point(x, y));
        }

        double lastX = LeftPad + (double)(history.Count - 1) / (MaxHistory - 1) * chartW;
        fillPoints?.Add(new Point(lastX, h));

        if (fillPoints is not null)
            canvas.Children.Add(new Polygon { Points = fillPoints, Fill = fillBrush });
        canvas.Children.Add(new Polyline { Points = linePoints, Stroke = lineBrush, StrokeThickness = 1.5 });
    }

    private static string FormatMem(double mb) =>
        mb == 0 ? "0" : mb >= 1024 ? $"{mb / 1024:F1}G" : $"{(int)mb}M";

    private static string FormatBps(long bps) => bps switch
    {
        >= 1024 * 1024 * 1024 => $"{bps / (1024.0 * 1024 * 1024):F1} GB/s",
        >= 1024 * 1024        => $"{bps / (1024.0 * 1024):F1} MB/s",
        >= 1024               => $"{bps / 1024.0:F0} KB/s",
        _                     => $"{bps} B/s"
    };
}
