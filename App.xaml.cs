using System.Windows;
using ProcessMonitor.Services;

namespace ProcessMonitor;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeManager.Apply(AppTheme.System);
    }
}

