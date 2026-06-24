using System.ComponentModel;
using System.Runtime.CompilerServices;
using ProcessMonitor.Models;

namespace ProcessMonitor.ViewModels;

public class ProcessViewModel : INotifyPropertyChanged
{
    private double _cpuPercent;
    private long _memoryMB;
    private int _threadCount;
    private int _handleCount;
    private bool _isResponding = true;
    private double _diskReadBps;
    private double _diskWriteBps;
    private bool _isNew;
    private bool _isTerminated;
    private bool _isSpike;
    private string _commandLine = "";

    public int PID { get; }
    public string Name { get; }
    public string FullPath { get; }
    public int? ParentPID { get; }
    public DateTime StartTime { get; }
    public bool HasFullPath => !string.IsNullOrEmpty(FullPath);
    public string StartTimeDisplay => StartTime == DateTime.MinValue ? "—"
        : StartTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    // Set by MainWindow when selection changes (lazy WMI fetch)
    public bool CommandLineFetched { get; set; }

    public double CpuPercent
    {
        get => _cpuPercent;
        set { _cpuPercent = value; OnPropertyChanged(); }
    }

    public long MemoryMB
    {
        get => _memoryMB;
        set { _memoryMB = value; OnPropertyChanged(); }
    }

    public int ThreadCount
    {
        get => _threadCount;
        set { _threadCount = value; OnPropertyChanged(); }
    }

    public int HandleCount
    {
        get => _handleCount;
        set { _handleCount = value; OnPropertyChanged(); }
    }

    public bool IsResponding
    {
        get => _isResponding;
        set
        {
            if (_isResponding == value) return;
            _isResponding = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Status));
        }
    }

    public string Status => _isResponding ? "Running" : "Not Responding";

    public double DiskReadBps
    {
        get => _diskReadBps;
        set { _diskReadBps = value; OnPropertyChanged(); OnPropertyChanged(nameof(DiskBpsDisplay)); }
    }

    public double DiskWriteBps
    {
        get => _diskWriteBps;
        set { _diskWriteBps = value; OnPropertyChanged(); OnPropertyChanged(nameof(DiskBpsDisplay)); }
    }

    public string DiskBpsDisplay
    {
        get
        {
            double total = _diskReadBps + _diskWriteBps;
            if (total < 1024) return total < 1 ? "" : $"{total:F0} B/s";
            if (total < 1024 * 1024) return $"{total / 1024:F0} KB/s";
            return $"{total / (1024 * 1024):F1} MB/s";
        }
    }

    public string CommandLine
    {
        get => _commandLine;
        set { _commandLine = value; OnPropertyChanged(); }
    }

    public bool IsNew
    {
        get => _isNew;
        set { _isNew = value; OnPropertyChanged(); }
    }

    public bool IsTerminated
    {
        get => _isTerminated;
        set { _isTerminated = value; OnPropertyChanged(); }
    }

    public bool IsSpike
    {
        get => _isSpike;
        set { _isSpike = value; OnPropertyChanged(); }
    }

    public ProcessViewModel(ProcessSnapshot snapshot)
    {
        PID = snapshot.PID;
        Name = snapshot.Name;
        FullPath = snapshot.FullPath;
        ParentPID = snapshot.ParentPID;
        StartTime = snapshot.StartTime;
        _cpuPercent = snapshot.CpuPercent;
        _memoryMB = snapshot.MemoryMB;
        _threadCount = snapshot.ThreadCount;
        _handleCount = snapshot.HandleCount;
        _isResponding = snapshot.IsResponding;
        _diskReadBps = snapshot.DiskReadBps;
        _diskWriteBps = snapshot.DiskWriteBps;
    }

    public void UpdateFrom(ProcessSnapshot snapshot)
    {
        CpuPercent = snapshot.CpuPercent;
        MemoryMB = snapshot.MemoryMB;
        ThreadCount = snapshot.ThreadCount;
        HandleCount = snapshot.HandleCount;
        IsResponding = snapshot.IsResponding;
        DiskReadBps = snapshot.DiskReadBps;
        DiskWriteBps = snapshot.DiskWriteBps;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
