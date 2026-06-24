namespace ProcessMonitor.Models;

public class ProcessSnapshot
{
    public int PID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public double CpuPercent { get; set; }
    public long MemoryMB { get; set; }
    public int? ParentPID { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime Timestamp { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public bool IsResponding { get; set; } = true;
    public ulong TotalDiskReadBytes { get; set; }
    public ulong TotalDiskWriteBytes { get; set; }
    public double DiskReadBps { get; set; }
    public double DiskWriteBps { get; set; }

    // Used internally by DiffEngine to compute CPU delta between snapshots
    public TimeSpan TotalProcessorTime { get; set; }
    public DateTime ProcessorTimeSample { get; set; }
}
