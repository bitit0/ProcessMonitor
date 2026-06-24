using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using ProcessMonitor.Models;
using ProcessMonitor.Services;
using ProcessMonitor.ViewModels;
using ProcessMonitor.Views;

namespace ProcessMonitor;

public partial class MainWindow : Window
{
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private bool _isExiting;
    private bool _minimizeToTray;
    private WindowSettings _windowSettings = new();

    public MainWindow()
    {
        InitializeComponent();
        LoadWindowSettings();
        InitTrayIcon();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        MinimizeToTrayItem.IsChecked = _minimizeToTray;
        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged += Vm_PropertyChanged;
            StartupWithWindowsItem.IsChecked = IsStartupEnabled();
        }
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainViewModel vm) return;

        if (e.PropertyName == nameof(MainViewModel.TrayTooltipText) && _notifyIcon is not null)
        {
            string tip = vm.TrayTooltipText;
            _notifyIcon.Text = tip.Length > 63 ? tip[..63] : tip;
        }

        // Update column headers with live totals
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.CpuColumnHeader):  CpuColumn.Header  = vm.CpuColumnHeader;  break;
            case nameof(MainViewModel.MemColumnHeader):  MemColumn.Header  = vm.MemColumnHeader;  break;
            case nameof(MainViewModel.DiskColumnHeader): DiskColumn.Header = vm.DiskColumnHeader; break;
            case nameof(MainViewModel.NetColumnHeader):  NetColumn.Header  = vm.NetColumnHeader;  break;
        }
    }

    // ── Theme ────────────────────────────────────────────────────────
    private void ThemeSystem_Click(object sender, RoutedEventArgs e) => ApplyTheme(AppTheme.System);
    private void ThemeLight_Click(object sender, RoutedEventArgs e)  => ApplyTheme(AppTheme.Light);
    private void ThemeDark_Click(object sender, RoutedEventArgs e)   => ApplyTheme(AppTheme.Dark);

    private void ApplyTheme(AppTheme theme)
    {
        ThemeManager.Apply(theme);
        ThemeSystem.IsChecked = theme == AppTheme.System;
        ThemeLight.IsChecked  = theme == AppTheme.Light;
        ThemeDark.IsChecked   = theme == AppTheme.Dark;
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        _isExiting = true;
        Close();
    }

    // ── Options ─────────────────────────────────────────────────────

    private void RefreshRate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || DataContext is not MainViewModel vm) return;
        int ms = int.Parse((string)mi.Tag);
        vm.SetRefreshRate(ms);
        Rate500.IsChecked  = ms == 500;
        Rate1000.IsChecked = ms == 1000;
        Rate2000.IsChecked = ms == 2000;
        Rate5000.IsChecked = ms == 5000;
    }

    private void PauseMonitoring_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.IsPaused = PauseMonitoringItem.IsChecked;
    }

    private void AlwaysOnTop_Click(object sender, RoutedEventArgs e)
    {
        Topmost = AlwaysOnTopItem.IsChecked;
    }

    private void MinimizeToTray_Click(object sender, RoutedEventArgs e)
    {
        _minimizeToTray = MinimizeToTrayItem.IsChecked;
        _windowSettings.MinimizeToTray = _minimizeToTray;
        // If user unchecks while window is hidden, restore it
        if (!_minimizeToTray && !IsVisible)
            ShowFromTray();
    }

    private void HighlightNewTerminated_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.HighlightProcesses = HighlightNewTerminatedItem.IsChecked;
    }

    private void MonitorSpikes_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.MonitorSpikes = MonitorSpikesItem.IsChecked;
    }

    private void SpikeSettings_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var dlg = new SpikeSettingsWindow(vm.SpikeThresholds.Clone()) { Owner = this };
        if (dlg.ShowDialog() == true)
            vm.SetSpikeThresholds(dlg.Result);
    }

    // ── Startup with Windows ─────────────────────────────────────────
    private const string StartupKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupName = "ProcessMonitor";

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupKeyPath);
        return key?.GetValue(StartupName) is not null;
    }

    private void StartupWithWindows_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupKeyPath, writable: true);
            if (key is null) return;

            if (StartupWithWindowsItem.IsChecked)
            {
                var exe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(exe))
                    key.SetValue(StartupName, exe);
            }
            else
            {
                key.DeleteValue(StartupName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            ShowError($"Could not set startup: {ex.Message}");
            StartupWithWindowsItem.IsChecked = !StartupWithWindowsItem.IsChecked;
        }
    }

    // ── Expand/collapse ──────────────────────────────────────────────

    private void ExpandButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton tb && tb.Tag is ProcessTreeNode node && DataContext is MainViewModel vm)
        {
            vm.ToggleNode(node);
            e.Handled = true;
        }
    }

    // ── Window lifecycle ─────────────────────────────────────────────

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (!_isExiting && _minimizeToTray)
        {
            e.Cancel = true;
            Hide();
            if (_notifyIcon is not null) _notifyIcon.Visible = true;
            return;
        }

        SaveWindowSettings();
        if (DataContext is MainViewModel vm)
            vm.Shutdown();
        _notifyIcon?.Dispose();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _minimizeToTray)
        {
            Hide();
            if (_notifyIcon is not null) _notifyIcon.Visible = true;
        }
        base.OnStateChanged(e);
    }

    // ── Keyboard shortcuts ───────────────────────────────────────────

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SearchBox.IsKeyboardFocused)
        {
            if (DataContext is MainViewModel vm) vm.SearchText = "";
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && !SearchBox.IsKeyboardFocused && GetSelectedNode() is not null)
        {
            EndProcess_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ExportCsv_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    // ── Tray icon ────────────────────────────────────────────────────

    private void InitTrayIcon()
    {
        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Text = "Process Monitor",
            Visible = false
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => ShowFromTray());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        if (_notifyIcon is not null) _notifyIcon.Visible = false;
    }

    private void ExitApp()
    {
        _isExiting = true;
        Close();
    }

    private static System.Drawing.Icon CreateTrayIcon()
    {
        using var bmp = new System.Drawing.Bitmap(16, 16,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.Clear(System.Drawing.Color.Transparent);
        g.FillRectangle(new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0, 120, 212)),
            0, 0, 16, 16);
        // Simple bar-chart graphic
        using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.LimeGreen);
        g.FillRectangle(brush,  2, 11, 2, 3);
        g.FillRectangle(brush,  5,  7, 2, 7);
        g.FillRectangle(brush,  8,  4, 2, 10);
        g.FillRectangle(brush, 11,  8, 2, 6);
        nint hIcon = bmp.GetHicon();
        return System.Drawing.Icon.FromHandle(hIcon);
    }

    // ── Window settings persistence ──────────────────────────────────

    private void LoadWindowSettings()
    {
        _windowSettings = WindowSettingsService.Load();
        Left   = _windowSettings.Left;
        Top    = _windowSettings.Top;
        Width  = _windowSettings.Width;
        Height = _windowSettings.Height;
        if (_windowSettings.IsMaximized)
            WindowState = WindowState.Maximized;
        _minimizeToTray = _windowSettings.MinimizeToTray;
        // MenuItem isn't available until InitializeComponent so apply in Loaded
    }

    private void SaveWindowSettings()
    {
        _windowSettings.IsMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            _windowSettings.Left   = Left;
            _windowSettings.Top    = Top;
            _windowSettings.Width  = Width;
            _windowSettings.Height = Height;
        }
        _windowSettings.SelectedTab = MainTabControl.SelectedIndex;
        _windowSettings.MinimizeToTray = _minimizeToTray;
        WindowSettingsService.Save(_windowSettings);
    }

    // ── Column sorting ───────────────────────────────────────────────

    private void ProcessGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;

        var dir = e.Column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        foreach (var col in ProcessGrid.Columns)
            col.SortDirection = null;
        e.Column.SortDirection = dir;

        if (DataContext is MainViewModel vm && e.Column.SortMemberPath is { Length: > 0 } path)
        {
            _windowSettings.SortColumn = path;
            _windowSettings.SortDescending = dir == ListSortDirection.Descending;
            vm.SetSort(path, dir == ListSortDirection.Descending);
        }
    }

    // ── Command line fetch (lazy, on selection change) ───────────────

    private void ProcessGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var node = vm.SelectedNode;
        if (node is null || node.Vm.CommandLineFetched) return;

        node.Vm.CommandLine = "Loading...";
        int pid = node.Vm.PID;
        var vmRef = node.Vm;

        Task.Run(() =>
        {
            string cmdLine = FetchCommandLine(pid);
            Dispatcher.Invoke(() =>
            {
                vmRef.CommandLine = cmdLine;
                vmRef.CommandLineFetched = true;
            });
        });
    }

    private static string FetchCommandLine(int pid)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
            using var results = searcher.Get();
            var obj = results.Cast<ManagementObject>().FirstOrDefault();
            return obj?["CommandLine"]?.ToString() ?? "(not available)";
        }
        catch { return "(not available)"; }
    }

    // ── Context menu ─────────────────────────────────────────────────

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
                catch { }
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

    // ── Export ───────────────────────────────────────────────────────

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
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var procRows = new List<string> { "PID,Name,CPU %,Memory (MB),Parent PID,Full Path,Snapshot Time" };
            foreach (var p in vm.GetAllProcesses())
                procRows.Add(string.Join(",", p.PID, CsvEscape(p.Name), p.CpuPercent.ToString("F1"),
                    p.MemoryMB, p.ParentPID?.ToString() ?? "", CsvEscape(p.FullPath), timestamp));

            var spikeRows = new List<string>();
            if (vm.MonitorSpikes && vm.SpikeHistory.Count > 0)
            {
                spikeRows.Add("Spike Time,Spike Message");
                foreach (var spike in vm.SpikeHistory)
                    spikeRows.Add($"{spike.Timestamp:yyyy-MM-dd HH:mm:ss},{CsvEscape(spike.Message)}");
            }

            const string emptyProc = ",,,,,,";
            bool hasSpikes = spikeRows.Count > 0;
            int rows = Math.Max(procRows.Count, spikeRows.Count);

            using var writer = new System.IO.StreamWriter(
                path: dlg.FileName, append: false,
                encoding: new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            for (int i = 0; i < rows; i++)
            {
                string proc  = i < procRows.Count  ? procRows[i]  : emptyProc;
                string spike = i < spikeRows.Count ? spikeRows[i] : "";
                writer.WriteLine(hasSpikes ? $"{proc},,{spike}" : proc);
            }
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
