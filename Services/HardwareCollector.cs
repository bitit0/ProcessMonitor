using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;
using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public sealed class HardwareCollector : IDisposable
{
    private readonly Computer _computer;
    private readonly PerformanceCounter? _cpuPerfCounter;
    private readonly PerformanceCounter[] _coreCounters;
    private readonly PerformanceCounter? _diskReadCounter;
    private readonly PerformanceCounter? _diskWriteCounter;
    private readonly double _cpuBaseMhz;
    private long _prevBytesSent;
    private long _prevBytesRecv;
    private DateTime _prevNetTime = DateTime.MinValue;

    public string RamType { get; } = "";
    public int RamSpeedMHz { get; }

    public HardwareCollector()
    {
        _computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
        try { _computer.Open(); }
        catch { }

        // Base clock from registry
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            _cpuBaseMhz = key?.GetValue("~MHz") is int mhz ? mhz : 0;
        }
        catch { }

        // % Processor Performance — same source Task Manager uses; exceeds 100% during boost
        try
        {
            _cpuPerfCounter = new PerformanceCounter(
                "Processor Information", "% Processor Performance", "_Total", readOnly: true);
            _cpuPerfCounter.NextValue(); // prime
        }
        catch { _cpuPerfCounter = null; }

        // Aggregate disk read/write bytes/sec
        try
        {
            _diskReadCounter  = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec",  "_Total", readOnly: true);
            _diskWriteCounter = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total", readOnly: true);
            _diskReadCounter.NextValue();
            _diskWriteCounter.NextValue();
        }
        catch { }

        // Per-core utilization counters
        try
        {
            var cat = new PerformanceCounterCategory("Processor Information");
            var instances = cat.GetInstanceNames()
                .Where(n => !n.Contains("_Total") && !n.StartsWith("_"))
                .OrderBy(n => n)
                .ToArray();
            _coreCounters = instances
                .Select(inst => new PerformanceCounter("Processor Information", "% Processor Time", inst, readOnly: true))
                .ToArray();
            foreach (var c in _coreCounters) c.NextValue(); // prime
        }
        catch { _coreCounters = []; }

        // RAM type and speed via WMI (one-time, runs at startup)
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT SMBIOSMemoryType, Speed FROM Win32_PhysicalMemory");
            foreach (ManagementObject obj in searcher.Get())
            {
                RamSpeedMHz = Math.Max(RamSpeedMHz, Convert.ToInt32(obj["Speed"] ?? 0));
                int typeCode = Convert.ToInt32(obj["SMBIOSMemoryType"] ?? 0);
                RamType = typeCode switch
                {
                    26 => "DDR4",
                    34 => "DDR5",
                    20 => "DDR2",
                    24 => "DDR3",
                    _ when string.IsNullOrEmpty(RamType) => $"Type {typeCode}",
                    _ => RamType
                };
                break; // use first DIMM for type/speed
            }
        }
        catch { }
    }

    public HardwareSnapshot Collect()
    {
        var snap = new HardwareSnapshot
        {
            RamType = RamType,
            RamSpeedMHz = RamSpeedMHz
        };

        try
        {
            foreach (var hw in _computer.Hardware)
            {
                try { hw.Update(); } catch { continue; }

                if (hw.HardwareType == HardwareType.Cpu)
                {
                    foreach (var s in hw.Sensors)
                    {
                        if (s.SensorType == SensorType.Temperature &&
                            s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) &&
                            s.Value.HasValue)
                        {
                            snap.CpuTempC = s.Value;
                        }
                    }
                }

                if (hw.HardwareType is HardwareType.GpuNvidia
                                     or HardwareType.GpuAmd
                                     or HardwareType.GpuIntel)
                {
                    foreach (var s in hw.Sensors)
                    {
                        if (s.SensorType == SensorType.Load &&
                            s.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase) &&
                            s.Value.HasValue)
                        {
                            snap.GpuPercent = s.Value.Value;
                        }

                        if (s.SensorType == SensorType.Temperature &&
                            s.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase) &&
                            s.Value.HasValue)
                        {
                            snap.GpuTempC = s.Value;
                        }
                    }
                }
            }
        }
        catch { }

        // CPU GHz = base × (% Processor Performance / 100) — matches Task Manager
        try
        {
            if (_cpuPerfCounter is not null && _cpuBaseMhz > 0)
                snap.CpuGhz = _cpuBaseMhz * _cpuPerfCounter.NextValue() / 100.0 / 1000.0;
        }
        catch { }

        // Aggregate disk
        try
        {
            if (_diskReadCounter is not null && _diskWriteCounter is not null)
            {
                snap.DiskReadBps  = (long)_diskReadCounter.NextValue();
                snap.DiskWriteBps = (long)_diskWriteCounter.NextValue();
            }
        }
        catch { }

        // Per-core utilization
        try
        {
            if (_coreCounters.Length > 0)
                snap.CoreUsages = _coreCounters.Select(c => (double)c.NextValue()).ToArray();
        }
        catch { snap.CoreUsages = []; }

        // Network bytes/sec
        try
        {
            var now = DateTime.UtcNow;
            long sent = 0, recv = 0;

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;
                var stats = ni.GetIPStatistics();
                sent += stats.BytesSent;
                recv += stats.BytesReceived;
            }

            if (_prevNetTime != DateTime.MinValue)
            {
                double secs = (now - _prevNetTime).TotalSeconds;
                if (secs > 0)
                {
                    snap.NetworkSendBps = Math.Max(0, (long)((sent - _prevBytesSent) / secs));
                    snap.NetworkRecvBps = Math.Max(0, (long)((recv - _prevBytesRecv) / secs));
                }
            }

            _prevBytesSent = sent;
            _prevBytesRecv = recv;
            _prevNetTime = now;
        }
        catch { }

        return snap;
    }

    public void Dispose()
    {
        try { _computer.Close(); } catch { }
        _cpuPerfCounter?.Dispose();
        _diskReadCounter?.Dispose();
        _diskWriteCounter?.Dispose();
        foreach (var c in _coreCounters) try { c.Dispose(); } catch { }
    }
}
