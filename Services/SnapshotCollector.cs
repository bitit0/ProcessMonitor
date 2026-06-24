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

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        nint processHandle,
        int processInformationClass,
        ref ProcessBasicInformation processInformation,
        int processInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll")]
    private static extern bool GetProcessIoCounters(nint hProcess, out IO_COUNTERS counters);

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

                try { snapshot.StartTime = p.StartTime.ToUniversalTime(); }
                catch { /* access denied for system processes — StartTime stays MinValue */ }

                try { snapshot.FullPath = p.MainModule?.FileName ?? string.Empty; }
                catch { /* access denied on path is non-fatal */ }

                try { snapshot.ThreadCount = p.Threads.Count; } catch { }
                try { snapshot.HandleCount = p.HandleCount; } catch { }
                try { snapshot.IsResponding = p.MainWindowHandle == IntPtr.Zero || p.Responding; }
                catch { snapshot.IsResponding = true; }

                try
                {
                    if (GetProcessIoCounters(p.Handle, out var io))
                    {
                        snapshot.TotalDiskReadBytes  = io.ReadTransferCount;
                        snapshot.TotalDiskWriteBytes = io.WriteTransferCount;
                    }
                }
                catch { }

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
