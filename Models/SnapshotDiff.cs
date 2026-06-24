namespace ProcessMonitor.Models;

public record SnapshotDiff
{
    public IReadOnlyList<ProcessSnapshot> New { get; init; } = [];
    public IReadOnlyList<ProcessSnapshot> Terminated { get; init; } = [];
    public IReadOnlyList<ProcessSnapshot> Updated { get; init; } = [];
    public DateTime Timestamp { get; init; }
    public HardwareSnapshot Hardware { get; init; } = new();
}
