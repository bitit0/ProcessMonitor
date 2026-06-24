namespace ProcessMonitor.Models;

public enum ProcessEventType { Started, Stopped, Updated, Spike }

public class ProcessEvent
{
    public DateTime Timestamp { get; init; }
    public ProcessEventType EventType { get; init; }
    public int PID { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string? Message { get; init; }

    public bool IsSpike => EventType == ProcessEventType.Spike;

    public string Description => EventType switch
    {
        ProcessEventType.Started => $"[+] {ProcessName} (PID {PID}) started",
        ProcessEventType.Stopped => $"[-] {ProcessName} (PID {PID}) stopped",
        ProcessEventType.Spike   => Message ?? "[!] Spike detected",
        _ => string.Empty
    };
}
