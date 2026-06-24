namespace ProcessMonitor.Models;

public class HardwareSnapshot
{
    public double CpuGhz { get; set; }
    public float? CpuTempC { get; set; }
    public double GpuPercent { get; set; }
    public float? GpuTempC { get; set; }
    public long NetworkSendBps { get; set; }
    public long NetworkRecvBps { get; set; }
    public long DiskReadBps { get; set; }
    public long DiskWriteBps { get; set; }
    public double[] CoreUsages { get; set; } = [];
    public string RamType { get; set; } = "";
    public int RamSpeedMHz { get; set; }
}
