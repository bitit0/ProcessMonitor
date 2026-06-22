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

    public ObservableCollection<ProcessTreeNode> TreeNodes { get; } = [];
    public ObservableCollection<ProcessEvent> EventLog { get; } = [];
    public ObservableCollection<ChartDataPoint> ChartHistory { get; } = [];
    public ICollectionView TreeNodesView { get; }

    public int ProcessCount => _processMap.Count;

    public string SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value;
            OnPropertyChanged();
            TreeNodesView.Refresh();
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
        TreeNodesView.Filter = obj =>
            obj is ProcessTreeNode node &&
            (string.IsNullOrWhiteSpace(_searchText) ||
             node.Vm.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase));

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
        RecordChartPoint();
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
        SyncFlatList(BuildFlatList());
        OnPropertyChanged(nameof(ProcessCount));
    }

    private void RebuildTreeStructure()
    {
        // Clear existing relationships
        foreach (var node in _treeNodeMap.Values)
        {
            node.Children.Clear();
            node.Parent = null;
        }

        // Assign parent-child relationships
        foreach (var node in _treeNodeMap.Values)
        {
            int? parentPid = node.Vm.ParentPID;
            if (parentPid.HasValue &&
                parentPid.Value != node.Vm.PID &&
                _treeNodeMap.TryGetValue(parentPid.Value, out var parentNode))
            {
                node.Parent = parentNode;
                parentNode.Children.Add(node);
            }
        }

        // Update HasChildren
        foreach (var node in _treeNodeMap.Values)
            node.HasChildren = node.Children.Count > 0;
    }

    private List<ProcessTreeNode> BuildFlatList()
    {
        var result = new List<ProcessTreeNode>(_treeNodeMap.Count);
        var visited = new HashSet<ProcessTreeNode>(_treeNodeMap.Count);

        foreach (var root in SortNodes(_treeNodeMap.Values.Where(n => n.Parent == null)))
            AppendNode(root, 0, result, visited);

        return result;
    }

    private void AppendNode(ProcessTreeNode node, int depth, List<ProcessTreeNode> result, HashSet<ProcessTreeNode> visited)
    {
        if (!visited.Add(node)) return; // cycle guard
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
            ("Vm.CpuPercent", false) => nodes.OrderBy(n => n.Vm.CpuPercent),
            ("Vm.CpuPercent", true)  => nodes.OrderByDescending(n => n.Vm.CpuPercent),
            ("Vm.MemoryMB",   false) => nodes.OrderBy(n => n.Vm.MemoryMB),
            ("Vm.MemoryMB",   true)  => nodes.OrderByDescending(n => n.Vm.MemoryMB),
            ("Vm.ParentPID",  false) => nodes.OrderBy(n => n.Vm.ParentPID ?? int.MaxValue),
            ("Vm.ParentPID",  true)  => nodes.OrderByDescending(n => n.Vm.ParentPID ?? int.MaxValue),
            (_,               false) => nodes.OrderBy(n => n.Vm.Name, StringComparer.OrdinalIgnoreCase),
            (_,               true)  => nodes.OrderByDescending(n => n.Vm.Name, StringComparer.OrdinalIgnoreCase),
        };

    private void SyncFlatList(List<ProcessTreeNode> target)
    {
        var targetSet = new HashSet<ProcessTreeNode>(target);

        // Remove nodes no longer in the target
        for (int i = TreeNodes.Count - 1; i >= 0; i--)
        {
            if (!targetSet.Contains(TreeNodes[i]))
                TreeNodes.RemoveAt(i);
        }

        // Insert or move nodes to match target order
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

    private void RecordChartPoint()
    {
        double totalCpu = Math.Min(100, _processMap.Values.Sum(v => v.CpuPercent));
        long totalMem = _processMap.Values.Sum(v => v.MemoryMB);

        ChartHistory.Add(new ChartDataPoint { CpuPercent = totalCpu, MemoryMB = totalMem });

        while (ChartHistory.Count > MaxChartHistory)
            ChartHistory.RemoveAt(0);
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

    public void Shutdown() => _engine.Stop();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
