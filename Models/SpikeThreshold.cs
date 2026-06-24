namespace ProcessMonitor.Models;

public class SpikeThreshold
{
    public bool   CpuEnabled    { get; set; }
    public double CpuPercent    { get; set; } = 80;

    public bool   MemoryEnabled { get; set; }
    public double MemoryPercent { get; set; } = 80;

    public bool   NetworkEnabled { get; set; }
    public double NetworkMBps    { get; set; } = 10;

    public bool   GpuEnabled    { get; set; }
    public double GpuPercent    { get; set; } = 80;

    public SpikeThreshold Clone() => (SpikeThreshold)MemberwiseClone();
}
