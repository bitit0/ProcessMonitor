namespace ProcessMonitor.Models;

public enum ProcessEventType { Started, Stopped, Updated }

public class ProcessEvent
{
    public DateTime Timestamp { get; init; }
    public ProcessEventType EventType { get; init; }
    public int PID { get; init; }
    public string ProcessName { get; init; } = string.Empty;

    public string Description => EventType switch
    {
        ProcessEventType.Started => $"[+] {ProcessName} (PID {PID}) started",
        ProcessEventType.Stopped => $"[-] {ProcessName} (PID {PID}) stopped",
        ProcessEventType.Updated => $"[~] {ProcessName} (PID {PID}) updated",
        _ => string.Empty
    };
}
