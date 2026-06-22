using Microsoft.Win32;
using System.Windows;

namespace ProcessMonitor.Services;

public enum AppTheme { System, Light, Dark }


public static class ThemeManager
{
    private static AppTheme _currentMode = AppTheme.System;
    public static AppTheme CurrentMode => _currentMode;

    public static void Apply(AppTheme mode)
    {
        _currentMode = mode;
        var effective = mode == AppTheme.System ? DetectSystemTheme() : mode;
        SwapDictionary(effective);
    }

    private static AppTheme DetectSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 1
                ? AppTheme.Light
                : AppTheme.Dark;
        }
        catch { return AppTheme.Dark; }
    }

    private static void SwapDictionary(AppTheme effective)
    {
        var uri = new Uri(
            effective == AppTheme.Light ? "Themes/Light.xaml" : "Themes/Dark.xaml",
            UriKind.Relative);

        var dicts = Application.Current.Resources.MergedDictionaries;
        var existing = dicts.FirstOrDefault(d => d.Source?.OriginalString.Contains("Themes/") == true);
        if (existing != null) dicts.Remove(existing);
        dicts.Add(new ResourceDictionary { Source = uri });
    }
}
