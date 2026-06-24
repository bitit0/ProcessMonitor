using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using ProcessMonitor.Models;
using ProcessMonitor.Services;


namespace ProcessMonitor.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    public const int MaxChartHistory = 120; // 2 minutes at 1s intervals

    private readonly MonitoringEngine _engine;
    private readonly Dictionary<int, ProcessViewModel> _processMap = [];
    private readonly Dictionary<int, ProcessTreeNode> _treeNodeMap = [];
    private string _searchText = string.Empty;
    private ProcessTreeNode? _selectedNode;
    private bool _isFirstDiff = true;
    private string _sortPath = "Vm.Name";
    private bool _sortDescending = false;
    private Dictionary<int, (double Cpu, long Mem)> _subtreeTotals = [];
    private bool _highlightProcesses = false;
    private bool _monitorSpikes = false;
    private SpikeThreshold _spikeThresholds = new();
    private readonly Dictionary<string, DateTime> _spikeCooldowns = [];
    private readonly List<ProcessEvent> _spikeHistory = [];

    public ObservableCollection<ProcessTreeNode> TreeNodes { get; } = [];
    public ObservableCollection<ProcessEvent> EventLog { get; } = [];
    public ObservableCollection<ChartDataPoint> ChartHistory { get; } = [];
    public ICollectionView TreeNodesView { get; }

    public int ProcessCount => _processMap.Count;

    // Current hardware snapshot values exposed for the tray tooltip and per-core display
    public double[] CurrentCoreUsages { get; private set; } = [];
    public float? CurrentCpuTempC { get; private set; }
    public string TrayTooltipText { get; private set; } = "Process Monitor";
    public string RamLabel { get; private set; } = "";

    // Column header labels (updated each tick so the header shows live totals)
    public string CpuColumnHeader  { get; private set; } = "CPU %";
    public string MemColumnHeader  { get; private set; } = "Memory (MB)";
    public string DiskColumnHeader { get; private set; } = "Disk";
    public string NetColumnHeader  { get; private set; } = "Network";

    public bool HighlightProcesses
    {
        get => _highlightProcesses;
        set { _highlightProcesses = value; OnPropertyChanged(); }
    }

    public bool IsPaused
    {
        get => _engine.IsPaused;
        set { _engine.IsPaused = value; OnPropertyChanged(); }
    }

    public void SetRefreshRate(int ms) => _engine.SetInterval(ms);

    public bool MonitorSpikes
    {
        get => _monitorSpikes;
        set { _monitorSpikes = value; OnPropertyChanged(); }
    }

    public SpikeThreshold SpikeThresholds => _spikeThresholds;
    public void SetSpikeThresholds(SpikeThreshold t) => _spikeThresholds = t;
    public IReadOnlyList<ProcessEvent> SpikeHistory => _spikeHistory;

    public string SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value;
            OnPropertyChanged();
            UpdateSearchHighlight();
        }
    }

    public ProcessTreeNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            _selectedNode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedProcess));
        }
    }

    public ProcessViewModel? SelectedProcess => _selectedNode?.Vm;

    public MainViewModel()
    {
        TreeNodesView = CollectionViewSource.GetDefaultView(TreeNodes);
        // No filter — all nodes show; dimming is handled by IsSearchMatch + row trigger

        _engine = new MonitoringEngine(intervalMs: 1000);
        _engine.DiffReady += OnDiffReady;
        _engine.Start();
    }

    private void OnDiffReady(SnapshotDiff diff)
    {
        Application.Current.Dispatcher.Invoke(() => ApplyDiff(diff));
    }

    private void ApplyDiff(SnapshotDiff diff)
    {
        foreach (var snapshot in diff.New)
        {
            var vm = new ProcessViewModel(snapshot) { IsNew = !_isFirstDiff };
            _processMap[snapshot.PID] = vm;
            _treeNodeMap[snapshot.PID] = new ProcessTreeNode(vm);
            if (!_isFirstDiff)
                AddEvent(ProcessEventType.Started, snapshot);
        }

        _isFirstDiff = false;

        foreach (var snapshot in diff.Updated)
        {
            if (_processMap.TryGetValue(snapshot.PID, out var vm))
            {
                vm.UpdateFrom(snapshot);
                vm.IsNew = false;
                vm.IsTerminated = false;
            }
        }

        foreach (var snapshot in diff.Terminated)
        {
            if (_processMap.TryGetValue(snapshot.PID, out var vm))
            {
                vm.IsTerminated = true;
                AddEvent(ProcessEventType.Stopped, snapshot);

                Task.Delay(2000).ContinueWith(_ =>
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        _processMap.Remove(snapshot.PID);
                        _treeNodeMap.Remove(snapshot.PID);
                        RebuildAndSync();
                    }));
            }
        }

        RebuildAndSync();
        RecordChartPoint(diff.Hardware);
        if (_monitorSpikes)
            CheckForSpikes(diff.Hardware);
    }

    public void SetSort(string path, bool descending)
    {
        _sortPath = path;
        _sortDescending = descending;
        SyncFlatList(BuildFlatList());
    }

    public void ToggleNode(ProcessTreeNode node)
    {
        if (!node.HasChildren) return;
        node.IsExpanded = !node.IsExpanded;
        SyncFlatList(BuildFlatList());
    }

    private void RebuildAndSync()
    {
        RebuildTreeStructure();
        UpdateSearchHighlight();
        SyncFlatList(BuildFlatList());
        OnPropertyChanged(nameof(ProcessCount));
    }

    private void UpdateSearchHighlight()
    {
        string text = _searchText;
        if (string.IsNullOrWhiteSpace(text))
        {
            foreach (var node in _treeNodeMap.Values)
                node.IsSearchMatch = true;
            return;
        }

        // Direct name matches
        foreach (var node in _treeNodeMap.Values)
            node.IsSearchMatch = node.Vm.Name.Contains(text, StringComparison.OrdinalIgnoreCase);

        // Propagate to ancestors so the tree path stays visible
        foreach (var node in _treeNodeMap.Values)
        {
            if (!node.IsSearchMatch) continue;
            var parent = node.Parent;
            while (parent is not null)
            {
                parent.IsSearchMatch = true;
                parent = parent.Parent;
            }
        }
    }

    // Processes that act as shell/launchers — their children are shown as roots
    private static readonly HashSet<string> LauncherProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "userinit.exe", "winlogon.exe"
    };

    private void RebuildTreeStructure()
    {
        foreach (var node in _treeNodeMap.Values)
        {
            node.Children.Clear();
            node.Parent = null;
        }

        foreach (var node in _treeNodeMap.Values)
        {
            int? parentPid = node.Vm.ParentPID;
            if (!parentPid.HasValue || parentPid.Value == node.Vm.PID)
                continue;

            if (!_treeNodeMap.TryGetValue(parentPid.Value, out var parentNode))
                continue;

            if (LauncherProcessNames.Contains(parentNode.Vm.Name))
                continue;

            if (parentNode.Vm.StartTime != DateTime.MinValue &&
                node.Vm.StartTime != DateTime.MinValue &&
                parentNode.Vm.StartTime > node.Vm.StartTime)
                continue;

            node.Parent = parentNode;
            parentNode.Children.Add(node);
        }

        foreach (var node in _treeNodeMap.Values)
            node.HasChildren = node.Children.Count > 0;
    }

    private List<ProcessTreeNode> BuildFlatList()
    {
        ComputeSubtotals();

        var result = new List<ProcessTreeNode>(_treeNodeMap.Count);
        var visited = new HashSet<ProcessTreeNode>(_treeNodeMap.Count);

        foreach (var root in SortNodes(_treeNodeMap.Values.Where(n => n.Parent == null)))
            AppendNode(root, 0, result, visited);

        return result;
    }

    private void ComputeSubtotals()
    {
        _subtreeTotals = new Dictionary<int, (double, long)>(_treeNodeMap.Count);
        foreach (var root in _treeNodeMap.Values.Where(n => n.Parent == null))
            ComputeSubtotalsRecursive(root);
        foreach (var node in _treeNodeMap.Values.Where(n => !_subtreeTotals.ContainsKey(n.Vm.PID)))
            _subtreeTotals[node.Vm.PID] = (node.Vm.CpuPercent, node.Vm.MemoryMB);
    }

    private (double Cpu, long Mem) ComputeSubtotalsRecursive(ProcessTreeNode node)
    {
        double cpu = node.Vm.CpuPercent;
        long mem = node.Vm.MemoryMB;
        foreach (var child in node.Children)
        {
            var (c, m) = ComputeSubtotalsRecursive(child);
            cpu += c;
            mem += m;
        }
        _subtreeTotals[node.Vm.PID] = (cpu, mem);
        return (cpu, mem);
    }

    private void AppendNode(ProcessTreeNode node, int depth, List<ProcessTreeNode> result, HashSet<ProcessTreeNode> visited)
    {
        if (!visited.Add(node)) return;
        node.Depth = depth;
        result.Add(node);

        if (node.IsExpanded && node.Children.Count > 0)
        {
            foreach (var child in SortNodes(node.Children))
                AppendNode(child, depth + 1, result, visited);
        }
    }

    private IEnumerable<ProcessTreeNode> SortNodes(IEnumerable<ProcessTreeNode> nodes) =>
        (_sortPath, _sortDescending) switch
        {
            ("Vm.PID",        false) => nodes.OrderBy(n => n.Vm.PID),
            ("Vm.PID",        true)  => nodes.OrderByDescending(n => n.Vm.PID),
            ("Vm.CpuPercent", false) => nodes.OrderBy(n => SubtreeCpu(n)),
            ("Vm.CpuPercent", true)  => nodes.OrderByDescending(n => SubtreeCpu(n)),
            ("Vm.MemoryMB",   false) => nodes.OrderBy(n => SubtreeMem(n)),
            ("Vm.MemoryMB",   true)  => nodes.OrderByDescending(n => SubtreeMem(n)),
            ("Vm.ParentPID",  false) => nodes.OrderBy(n => n.Vm.ParentPID ?? int.MaxValue),
            ("Vm.ParentPID",  true)  => nodes.OrderByDescending(n => n.Vm.ParentPID ?? int.MaxValue),
            (_,               false) => nodes.OrderBy(n => n.Vm.Name, StringComparer.OrdinalIgnoreCase),
            (_,               true)  => nodes.OrderByDescending(n => n.Vm.Name, StringComparer.OrdinalIgnoreCase),
        };

    private double SubtreeCpu(ProcessTreeNode n) =>
        _subtreeTotals.TryGetValue(n.Vm.PID, out var t) ? t.Cpu : n.Vm.CpuPercent;

    private long SubtreeMem(ProcessTreeNode n) =>
        _subtreeTotals.TryGetValue(n.Vm.PID, out var t) ? t.Mem : n.Vm.MemoryMB;

    private void SyncFlatList(List<ProcessTreeNode> target)
    {
        var targetSet = new HashSet<ProcessTreeNode>(target);

        for (int i = TreeNodes.Count - 1; i >= 0; i--)
        {
            if (!targetSet.Contains(TreeNodes[i]))
                TreeNodes.RemoveAt(i);
        }

        for (int i = 0; i < target.Count; i++)
        {
            var node = target[i];
            int currentIdx = TreeNodes.IndexOf(node);
            if (currentIdx == -1)
                TreeNodes.Insert(i, node);
            else if (currentIdx != i)
                TreeNodes.Move(currentIdx, i);
        }
    }

    private void RecordChartPoint(HardwareSnapshot hw)
    {
        double totalCpu = Math.Min(100, _processMap.Values.Sum(v => v.CpuPercent));
        long totalMem = _processMap.Values.Sum(v => v.MemoryMB);
        long totalRamMB = (long)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024L * 1024));
        double memPct = totalRamMB > 0 ? (double)totalMem / totalRamMB * 100.0 : 0;

        ChartHistory.Add(new ChartDataPoint
        {
            CpuPercent     = totalCpu,
            MemoryMB       = totalMem,
            CpuGhz         = hw.CpuGhz,
            CpuTempC       = hw.CpuTempC,
            GpuPercent     = hw.GpuPercent,
            GpuTempC       = hw.GpuTempC,
            NetworkSendBps = hw.NetworkSendBps,
            NetworkRecvBps = hw.NetworkRecvBps,
            DiskReadBps    = hw.DiskReadBps,
            DiskWriteBps   = hw.DiskWriteBps,
            CoreUsages     = hw.CoreUsages,
        });

        while (ChartHistory.Count > MaxChartHistory)
            ChartHistory.RemoveAt(0);

        CurrentCoreUsages = hw.CoreUsages;
        CurrentCpuTempC = hw.CpuTempC;
        OnPropertyChanged(nameof(CurrentCoreUsages));
        OnPropertyChanged(nameof(CurrentCpuTempC));

        if (string.IsNullOrEmpty(RamLabel) && !string.IsNullOrEmpty(hw.RamType))
        {
            RamLabel = hw.RamSpeedMHz > 0 ? $"| {hw.RamType} @ {hw.RamSpeedMHz} MHz" : $"| {hw.RamType}";
            OnPropertyChanged(nameof(RamLabel));
        }

        // Update column headers with live totals
        CpuColumnHeader  = $"CPU ({totalCpu:F0}%)";
        MemColumnHeader  = $"Memory ({memPct:F0}%)";
        DiskColumnHeader = $"Disk ({FormatBps(hw.DiskReadBps + hw.DiskWriteBps)})";
        NetColumnHeader  = $"Network (↑{FormatBps(hw.NetworkSendBps)} ↓{FormatBps(hw.NetworkRecvBps)})";
        OnPropertyChanged(nameof(CpuColumnHeader));
        OnPropertyChanged(nameof(MemColumnHeader));
        OnPropertyChanged(nameof(DiskColumnHeader));
        OnPropertyChanged(nameof(NetColumnHeader));

        string memStr = totalMem >= 1024 ? $"{totalMem / 1024.0:F1} GB" : $"{totalMem} MB";
        TrayTooltipText = $"Process Monitor — CPU: {totalCpu:F0}% | RAM: {memStr} ({memPct:F0}%)";
        OnPropertyChanged(nameof(TrayTooltipText));
    }

    private void AddEvent(ProcessEventType type, ProcessSnapshot snapshot)
    {
        var ev = new ProcessEvent
        {
            Timestamp = DateTime.Now,
            EventType = type,
            PID = snapshot.PID,
            ProcessName = snapshot.Name
        };

        EventLog.Insert(0, ev);

        while (EventLog.Count > 500)
            EventLog.RemoveAt(EventLog.Count - 1);
    }

    public IEnumerable<ProcessViewModel> GetAllProcesses()
        => _processMap.Values.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<int> GetSubtreePids(ProcessTreeNode root)
    {
        var pids = new List<int>();
        CollectPids(root, pids, []);
        return pids;
    }

    private static void CollectPids(ProcessTreeNode node, List<int> pids, HashSet<ProcessTreeNode> visited)
    {
        if (!visited.Add(node)) return;
        pids.Add(node.Vm.PID);
        foreach (var child in node.Children)
            CollectPids(child, pids, visited);
    }

    private void CheckForSpikes(HardwareSnapshot hw)
    {
        var t = _spikeThresholds;
        var now = DateTime.Now;

        if (t.CpuEnabled)
        {
            double cpu = Math.Min(100, _processMap.Values.Sum(v => v.CpuPercent));
            if (cpu >= t.CpuPercent && CanFireSpike("CPU", now))
            {
                var top = _processMap.Values.OrderByDescending(v => v.CpuPercent).FirstOrDefault();
                string proc = top is not null ? $"  — top: {top.Name} ({top.CpuPercent:F1}%)" : "";
                FireSpike($"[!] CPU spike: {cpu:F1}% (threshold: {t.CpuPercent:F0}%){proc}", now, top);
            }
        }

        if (t.MemoryEnabled)
        {
            long mem = _processMap.Values.Sum(v => v.MemoryMB);
            long totalMB = (long)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024L * 1024));
            double pct = totalMB > 0 ? (double)mem / totalMB * 100.0 : 0;
            if (pct >= t.MemoryPercent && CanFireSpike("Memory", now))
            {
                var top = _processMap.Values.OrderByDescending(v => v.MemoryMB).FirstOrDefault();
                string proc = top is not null ? $"  — top: {top.Name} ({top.MemoryMB} MB)" : "";
                FireSpike($"[!] Memory spike: {pct:F1}% (threshold: {t.MemoryPercent:F0}%){proc}", now, top);
            }
        }

        if (t.NetworkEnabled)
        {
            double mbps = (hw.NetworkSendBps + hw.NetworkRecvBps) / (1024.0 * 1024);
            if (mbps >= t.NetworkMBps && CanFireSpike("Network", now))
                FireSpike($"[!] Network spike: {mbps:F1} MB/s (threshold: {t.NetworkMBps:F0} MB/s)", now, null);
        }

        if (t.GpuEnabled && hw.GpuPercent >= t.GpuPercent && CanFireSpike("GPU", now))
            FireSpike($"[!] GPU spike: {hw.GpuPercent:F1}% (threshold: {t.GpuPercent:F0}%)", now, null);
    }

    private bool CanFireSpike(string key, DateTime now)
    {
        if (_spikeCooldowns.TryGetValue(key, out var last) && (now - last).TotalSeconds < 10)
            return false;
        _spikeCooldowns[key] = now;
        return true;
    }

    private void FireSpike(string message, DateTime now, ProcessViewModel? topProcess)
    {
        var ev = new ProcessEvent
        {
            Timestamp = now,
            EventType = ProcessEventType.Spike,
            Message   = message
        };
        EventLog.Insert(0, ev);
        _spikeHistory.Add(ev);
        while (EventLog.Count > 500) EventLog.RemoveAt(EventLog.Count - 1);

        if (topProcess is not null && _treeNodeMap.TryGetValue(topProcess.PID, out _))
        {
            topProcess.IsSpike = true;
            Task.Delay(2000).ContinueWith(_ =>
                Application.Current.Dispatcher.Invoke(() => topProcess.IsSpike = false));
        }
    }

    private static string FormatBps(long bps) => bps switch
    {
        >= 1024 * 1024 * 1024 => $"{bps / (1024.0 * 1024 * 1024):F1} GB/s",
        >= 1024 * 1024        => $"{bps / (1024.0 * 1024):F1} MB/s",
        >= 1024               => $"{bps / 1024.0:F0} KB/s",
        _                     => $"{bps} B/s"
    };

    public void Shutdown() => _engine.Stop();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
