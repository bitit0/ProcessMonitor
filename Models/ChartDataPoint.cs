namespace ProcessMonitor.Models;

public class ChartDataPoint
{
    public double CpuPercent { get; init; }
    public long MemoryMB { get; init; }
    public double CpuGhz { get; init; }
    public float? CpuTempC { get; init; }
    public double GpuPercent { get; init; }
    public float? GpuTempC { get; init; }
    public long NetworkSendBps { get; init; }
    public long NetworkRecvBps { get; init; }
    public long DiskReadBps { get; init; }
    public long DiskWriteBps { get; init; }
    public double[] CoreUsages { get; init; } = [];
}
