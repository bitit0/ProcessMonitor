using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using ProcessMonitor.Services;
using ProcessMonitor.ViewModels;

namespace ProcessMonitor;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void ThemeSystem_Checked(object sender, RoutedEventArgs e) => ThemeManager.Apply(AppTheme.System);
    private void ThemeLight_Checked(object sender, RoutedEventArgs e)  => ThemeManager.Apply(AppTheme.Light);
    private void ThemeDark_Checked(object sender, RoutedEventArgs e)   => ThemeManager.Apply(AppTheme.Dark);

    private void ExpandButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton tb && tb.Tag is ProcessTreeNode node && DataContext is MainViewModel vm)
        {
            vm.ToggleNode(node);
            e.Handled = true;
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.Shutdown();
    }

    // ── Column sorting ──────────────────────────────────────────────

    private void ProcessGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true; // suppress default sort so it doesn't flatten the tree

        var dir = e.Column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        foreach (var col in ProcessGrid.Columns)
            col.SortDirection = null;
        e.Column.SortDirection = dir;

        if (DataContext is MainViewModel vm && e.Column.SortMemberPath is { Length: > 0 } path)
            vm.SetSort(path, dir == ListSortDirection.Descending);
    }

    // ── Context menu ────────────────────────────────────────────────

    private bool _rowRightClicked;

    private ProcessTreeNode? GetSelectedNode()
        => (DataContext as MainViewModel)?.SelectedNode;

    private void ProcessGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(ProcessGrid, e.OriginalSource as DependencyObject) is DataGridRow row)
        {
            row.IsSelected = true;
            _rowRightClicked = true;
        }
        else
        {
            _rowRightClicked = false;
        }
    }

    private void ProcessGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (!_rowRightClicked) e.Handled = true;
    }

    private void EndProcess_Click(object sender, RoutedEventArgs e)
    {
        var node = GetSelectedNode();
        if (node is null) return;

        var result = MessageBox.Show(
            $"End process '{node.Vm.Name}' (PID {node.Vm.PID})?\n\nUnsaved data may be lost.",
            "End Process", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            try { Process.GetProcessById(node.Vm.PID).Kill(); }
            catch (Exception ex) { ShowError(ex.Message); }
        }
    }

    private void EndProcessTree_Click(object sender, RoutedEventArgs e)
    {
        var node = GetSelectedNode();
        if (DataContext is not MainViewModel vm || node is null) return;

        var pids = vm.GetSubtreePids(node);
        int childCount = pids.Count - 1;
        string childDesc = childCount > 0
            ? $" and {childCount} child process{(childCount == 1 ? "" : "es")}"
            : "";

        var result = MessageBox.Show(
            $"End '{node.Vm.Name}'{childDesc} (PID {node.Vm.PID})?\n\nUnsaved data may be lost.",
            "End Process Tree", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            foreach (var pid in pids)
            {
                try { Process.GetProcessById(pid).Kill(); }
                catch { /* already exited or access denied */ }
            }
        }
    }

    private void OpenFileLocation_Click(object sender, RoutedEventArgs e)
    {
        var node = GetSelectedNode();
        if (node is null) return;

        if (string.IsNullOrEmpty(node.Vm.FullPath))
        {
            MessageBox.Show("File path is not available for this process.", "Open File Location",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try { Process.Start("explorer.exe", $"/select,\"{node.Vm.FullPath}\""); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void CopyPid_Click(object sender, RoutedEventArgs e)
    {
        var node = GetSelectedNode();
        if (node is not null)
            Clipboard.SetText(node.Vm.PID.ToString());
    }

    private void CopyFullPath_Click(object sender, RoutedEventArgs e)
    {
        var node = GetSelectedNode();
        if (node is null) return;

        if (string.IsNullOrEmpty(node.Vm.FullPath))
        {
            MessageBox.Show("File path is not available for this process.", "Copy Full Path",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Clipboard.SetText(node.Vm.FullPath);
    }

    private void SetPriority_Click(object sender, RoutedEventArgs e)
    {
        var node = GetSelectedNode();
        if (node is null || sender is not MenuItem mi) return;

        var priority = mi.Tag?.ToString() switch
        {
            "Idle"        => ProcessPriorityClass.Idle,
            "BelowNormal" => ProcessPriorityClass.BelowNormal,
            "Normal"      => ProcessPriorityClass.Normal,
            "AboveNormal" => ProcessPriorityClass.AboveNormal,
            "High"        => ProcessPriorityClass.High,
            _             => ProcessPriorityClass.Normal
        };

        try { Process.GetProcessById(node.Vm.PID).PriorityClass = priority; }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    // ── Export ──────────────────────────────────────────────────────

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var dlg = new SaveFileDialog
        {
            Filter   = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = $"processes_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            using var writer = new StreamWriter(dlg.FileName, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            writer.WriteLine("PID,Name,CPU %,Memory (MB),Parent PID,Full Path,Snapshot Time");

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            foreach (var p in vm.GetAllProcesses())
            {
                writer.WriteLine(string.Join(",",
                    p.PID,
                    CsvEscape(p.Name),
                    p.CpuPercent.ToString("F1"),
                    p.MemoryMB,
                    p.ParentPID?.ToString() ?? "",
                    CsvEscape(p.FullPath),
                    timestamp));
            }

            MessageBox.Show($"Exported {vm.ProcessCount} processes to:\n{dlg.FileName}",
                "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowError($"Export failed: {ex.Message}");
        }
    }

    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static void ShowError(string message) =>
        MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
}
