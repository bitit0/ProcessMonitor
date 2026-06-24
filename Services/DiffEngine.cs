using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public static class DiffEngine
{
    public static SnapshotDiff Compute(
        IReadOnlyDictionary<int, ProcessSnapshot> previous,
        ProcessSnapshot[] current)
    {
        var currentMap = current.ToDictionary(s => s.PID);
        var now = DateTime.UtcNow;

        var newProcesses = new List<ProcessSnapshot>();
        var terminated = new List<ProcessSnapshot>();
        var updated = new List<ProcessSnapshot>();

        foreach (var snapshot in current)
        {
            if (!previous.TryGetValue(snapshot.PID, out var prev))
            {
                newProcesses.Add(snapshot);
            }
            else
            {
                snapshot.CpuPercent  = ComputeCpuPercent(prev, snapshot);
                snapshot.DiskReadBps  = ComputeDeltaBps(prev.TotalDiskReadBytes,  snapshot.TotalDiskReadBytes,  prev, snapshot);
                snapshot.DiskWriteBps = ComputeDeltaBps(prev.TotalDiskWriteBytes, snapshot.TotalDiskWriteBytes, prev, snapshot);
                updated.Add(snapshot);
            }
        }

        foreach (var pid in previous.Keys)
        {
            if (!currentMap.ContainsKey(pid))
                terminated.Add(previous[pid]);
        }

        return new SnapshotDiff
        {
            New = newProcesses,
            Terminated = terminated,
            Updated = updated,
            Timestamp = now
        };
    }

    private static double ComputeCpuPercent(ProcessSnapshot prev, ProcessSnapshot current)
    {
        var cpuDelta  = (current.TotalProcessorTime - prev.TotalProcessorTime).TotalMilliseconds;
        var timeDelta = (current.ProcessorTimeSample - prev.ProcessorTimeSample).TotalMilliseconds;
        if (timeDelta <= 0) return 0;
        return Math.Round(cpuDelta / timeDelta / Environment.ProcessorCount * 100, 1);
    }

    private static double ComputeDeltaBps(ulong prevBytes, ulong currBytes,
        ProcessSnapshot prev, ProcessSnapshot current)
    {
        if (currBytes < prevBytes) return 0; // counter reset or new process
        double secs = (current.ProcessorTimeSample - prev.ProcessorTimeSample).TotalSeconds;
        if (secs <= 0) return 0;
        return (currBytes - prevBytes) / secs;
    }
}
