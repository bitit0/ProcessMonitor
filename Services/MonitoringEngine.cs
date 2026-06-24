using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public class MonitoringEngine
{
    private int _intervalMs;
    private bool _isPaused;
    private CancellationTokenSource? _cts;
    private Dictionary<int, ProcessSnapshot> _previousSnapshot = [];
    private readonly HardwareCollector _hardware = new();

    public event Action<SnapshotDiff>? DiffReady;

    public MonitoringEngine(int intervalMs = 1000)
    {
        _intervalMs = intervalMs;
    }

    public bool IsPaused { get => _isPaused; set => _isPaused = value; }

    public void SetInterval(int ms) => _intervalMs = ms;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        Task.Run(() => RunLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _hardware.Dispose();
    }

    private async Task RunLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (!_isPaused)
            {
                var current = SnapshotCollector.Collect();
                var diff = DiffEngine.Compute(_previousSnapshot, current);
                _previousSnapshot = current.ToDictionary(s => s.PID);
                diff = diff with { Hardware = _hardware.Collect() };
                DiffReady?.Invoke(diff);
            }

            try { await Task.Delay(_intervalMs, token); }
            catch (TaskCanceledException) { break; }
        }
    }
}
