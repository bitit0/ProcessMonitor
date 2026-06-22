using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public class MonitoringEngine
{
    private readonly int _intervalMs;
    private CancellationTokenSource? _cts;
    private Dictionary<int, ProcessSnapshot> _previousSnapshot = [];

    public event Action<SnapshotDiff>? DiffReady;

    public MonitoringEngine(int intervalMs = 1000)
    {
        _intervalMs = intervalMs;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        Task.Run(() => RunLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    private async Task RunLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var current = SnapshotCollector.Collect();
            var diff = DiffEngine.Compute(_previousSnapshot, current);

            _previousSnapshot = current.ToDictionary(s => s.PID);

            DiffReady?.Invoke(diff);

            try { await Task.Delay(_intervalMs, token); }
            catch (TaskCanceledException) { break; }
        }
    }
}
