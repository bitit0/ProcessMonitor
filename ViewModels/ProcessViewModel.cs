using System.ComponentModel;
using System.Runtime.CompilerServices;
using ProcessMonitor.Models;

namespace ProcessMonitor.ViewModels;

public class ProcessViewModel : INotifyPropertyChanged
{
    private double _cpuPercent;
    private long _memoryMB;
    private bool _isNew;
    private bool _isTerminated;

    public int PID { get; }
    public string Name { get; }
    public string FullPath { get; }
    public int? ParentPID { get; }
    public bool HasFullPath => !string.IsNullOrEmpty(FullPath);

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

    public ProcessViewModel(ProcessSnapshot snapshot)
    {
        PID = snapshot.PID;
        Name = snapshot.Name;
        FullPath = snapshot.FullPath;
        ParentPID = snapshot.ParentPID;
        CpuPercent = snapshot.CpuPercent;
        MemoryMB = snapshot.MemoryMB;
    }

    public void UpdateFrom(ProcessSnapshot snapshot)
    {
        CpuPercent = snapshot.CpuPercent;
        MemoryMB = snapshot.MemoryMB;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
