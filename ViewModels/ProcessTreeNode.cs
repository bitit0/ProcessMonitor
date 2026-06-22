using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ProcessMonitor.ViewModels;

public class ProcessTreeNode : INotifyPropertyChanged
{
    private bool _isExpanded = true;
    private bool _hasChildren;

    public ProcessViewModel Vm { get; }
    public int Depth { get; internal set; }
    public ProcessTreeNode? Parent { get; internal set; }
    public List<ProcessTreeNode> Children { get; } = [];

    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); }
    }

    public bool HasChildren
    {
        get => _hasChildren;
        internal set { _hasChildren = value; OnPropertyChanged(); }
    }

    // Forwarded for DataGrid row-style triggers
    public bool IsNew => Vm.IsNew;
    public bool IsTerminated => Vm.IsTerminated;

    public ProcessTreeNode(ProcessViewModel vm)
    {
        Vm = vm;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(IsNew) or nameof(IsTerminated))
                OnPropertyChanged(e.PropertyName);
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
