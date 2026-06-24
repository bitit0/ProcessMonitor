namespace ProcessMonitor.Models;

public class WindowSettings
{
    public double Left { get; set; } = 100;
    public double Top { get; set; } = 100;
    public double Width { get; set; } = 1100;
    public double Height { get; set; } = 700;
    public bool IsMaximized { get; set; }
    public string SortColumn { get; set; } = "Vm.Name";
    public bool SortDescending { get; set; }
    public int SelectedTab { get; set; }
    public bool MinimizeToTray { get; set; }
}
