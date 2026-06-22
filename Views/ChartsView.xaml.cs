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
    private const double LeftPad = 44;

    private static readonly Brush CpuLine = Freeze(Color.FromRgb(0, 200, 83));
    private static readonly Brush CpuFill = Freeze(Color.FromArgb(35, 0, 200, 83));
    private static readonly Brush MemLine = Freeze(Color.FromRgb(41, 121, 255));
    private static readonly Brush MemFill = Freeze(Color.FromArgb(35, 41, 121, 255));
    private static readonly Brush GridLines = Freeze(Color.FromArgb(45, 128, 128, 128));
    private static readonly Brush Labels = Freeze(Color.FromRgb(130, 130, 130));
    private static readonly FontFamily Mono = new("Consolas");

    private static Brush Freeze(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    private MainViewModel? _vm;

    public ChartsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
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

        double cpu = h.Count > 0 ? h[^1].CpuPercent : 0;
        long mem = h.Count > 0 ? h[^1].MemoryMB : 0;
        CpuCurrentLabel.Text = $"— {cpu:F1}%";
        MemCurrentLabel.Text = mem >= 1024
            ? $"— {mem / 1024.0:F1} GB"
            : $"— {mem:N0} MB";

        DrawChart(CpuCanvas, h, p => p.CpuPercent, 100,
            v => $"{v:F0}%", CpuLine, CpuFill);

        double memMax = h.Count > 0
            ? Math.Max(h.Max(p => (double)p.MemoryMB) * 1.15, 512)
            : 1024;
        DrawChart(MemCanvas, h, p => p.MemoryMB, memMax,
            v => FormatMem(v), MemLine, MemFill);
    }

    private void DrawChart(
        Canvas canvas,
        IReadOnlyList<ChartDataPoint> history,
        Func<ChartDataPoint, double> getValue,
        double maxValue,
        Func<double, string> labelFormat,
        Brush lineBrush,
        Brush fillBrush)
    {
        canvas.Children.Clear();
        double w = canvas.ActualWidth;
        double h = canvas.ActualHeight;
        if (w <= 0 || h <= 0 || maxValue <= 0) return;

        double chartW = w - LeftPad;

        // Y-axis grid lines and labels at 0%, 25%, 50%, 75%, 100%
        foreach (var frac in new[] { 1.0, 0.75, 0.5, 0.25, 0.0 })
        {
            double y = h * (1.0 - frac);

            if (frac > 0.0 && frac < 1.0)
            {
                canvas.Children.Add(new Line
                {
                    X1 = LeftPad, Y1 = y, X2 = w, Y2 = y,
                    Stroke = GridLines,
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 4, 4 }
                });
            }

            var label = new TextBlock
            {
                Text = labelFormat(frac * maxValue),
                Foreground = Labels,
                FontSize = 10,
                FontFamily = Mono,
                Width = LeftPad - 4,
                TextAlignment = TextAlignment.Right
            };
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, Math.Clamp(y - 7, 0, h - 14));
            canvas.Children.Add(label);
        }

        if (history.Count < 2) return;

        var linePoints = new PointCollection(history.Count);
        var fillPoints = new PointCollection(history.Count + 2);
        fillPoints.Add(new Point(LeftPad, h));

        for (int i = 0; i < history.Count; i++)
        {
            double x = LeftPad + (double)i / (MaxHistory - 1) * chartW;
            double val = Math.Clamp(getValue(history[i]), 0, maxValue);
            double y = Math.Clamp(h - val / maxValue * h, 0, h);
            linePoints.Add(new Point(x, y));
            fillPoints.Add(new Point(x, y));
        }

        double lastX = LeftPad + (double)(history.Count - 1) / (MaxHistory - 1) * chartW;
        fillPoints.Add(new Point(lastX, h));

        canvas.Children.Add(new Polygon { Points = fillPoints, Fill = fillBrush });
        canvas.Children.Add(new Polyline { Points = linePoints, Stroke = lineBrush, StrokeThickness = 1.5 });
    }

    private static string FormatMem(double mb) =>
        mb == 0 ? "0" : mb >= 1024 ? $"{mb / 1024:F1}G" : $"{(int)mb}M";
}
