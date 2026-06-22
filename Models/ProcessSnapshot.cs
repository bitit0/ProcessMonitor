namespace ProcessMonitor.Models;

public class ProcessSnapshot
{
    public int PID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public double CpuPercent { get; set; }
    public long MemoryMB { get; set; }
    public int? ParentPID { get; set; }
    public DateTime Timestamp { get; set; }

    // Used internally by DiffEngine to compute CPU delta between snapshots
    public TimeSpan TotalProcessorTime { get; set; }
    public DateTime ProcessorTimeSample { get; set; }
}
