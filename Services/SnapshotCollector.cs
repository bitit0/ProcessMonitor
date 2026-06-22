using System.Diagnostics;
using System.Runtime.InteropServices;
using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public static class SnapshotCollector
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public nint Reserved1;
        public nint PebBaseAddress;
        public nint Reserved2_0;
        public nint Reserved2_1;
        public nint UniqueProcessId;
        public nint InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        nint processHandle,
        int processInformationClass,
        ref ProcessBasicInformation processInformation,
        int processInformationLength,
        out int returnLength);

    public static ProcessSnapshot[] Collect()
    {
        var now = DateTime.UtcNow;
        var processes = Process.GetProcesses();
        var snapshots = new List<ProcessSnapshot>(processes.Length);

        foreach (var p in processes)
        {
            try
            {
                var snapshot = new ProcessSnapshot
                {
                    PID = p.Id,
                    Name = p.ProcessName + ".exe",
                    MemoryMB = p.WorkingSet64 / (1024 * 1024),
                    ParentPID = GetParentPid(p),
                    Timestamp = now,
                    TotalProcessorTime = p.TotalProcessorTime,
                    ProcessorTimeSample = now,
                };

                try { snapshot.FullPath = p.MainModule?.FileName ?? string.Empty; }
                catch { /* access denied on path is non-fatal */ }

                snapshots.Add(snapshot);
            }
            catch
            {
                // Process exited or access denied — skip
            }
            finally
            {
                p.Dispose();
            }
        }

        return [.. snapshots];
    }

    private static int? GetParentPid(Process process)
    {
        try
        {
            var info = new ProcessBasicInformation();
            int status = NtQueryInformationProcess(process.Handle, 0, ref info, Marshal.SizeOf(info), out _);
            if (status == 0)
                return (int)info.InheritedFromUniqueProcessId;
        }
        catch { /* access denied or process gone */ }
        return null;
    }
}
