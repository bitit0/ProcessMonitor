using System.Windows;
using ProcessMonitor.Models;

namespace ProcessMonitor.Views;

public partial class SpikeSettingsWindow : Window
{
    public SpikeThreshold Result { get; private set; } = new();

    public SpikeSettingsWindow(SpikeThreshold current)
    {
        InitializeComponent();
        CpuCheck.IsChecked  = current.CpuEnabled;
        CpuValue.Text       = current.CpuPercent.ToString("F0");
        MemCheck.IsChecked  = current.MemoryEnabled;
        MemValue.Text       = current.MemoryPercent.ToString("F0");
        NetCheck.IsChecked  = current.NetworkEnabled;
        NetValue.Text       = current.NetworkMBps.ToString("F0");
        GpuCheck.IsChecked  = current.GpuEnabled;
        GpuValue.Text       = current.GpuPercent.ToString("F0");
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Result = new SpikeThreshold
        {
            CpuEnabled     = CpuCheck.IsChecked == true,
            CpuPercent     = Parse(CpuValue.Text,  80),
            MemoryEnabled  = MemCheck.IsChecked == true,
            MemoryPercent  = Parse(MemValue.Text,  80),
            NetworkEnabled = NetCheck.IsChecked == true,
            NetworkMBps    = Parse(NetValue.Text,  10),
            GpuEnabled     = GpuCheck.IsChecked == true,
            GpuPercent     = Parse(GpuValue.Text,  80),
        };
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private static double Parse(string s, double fallback) =>
        double.TryParse(s, out var v) && v > 0 ? v : fallback;
}
