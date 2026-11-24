using System.Security;
using Microsoft.Win32;

namespace FocusTimer.App.Native;

/// <summary>"Iniciar con Windows": HKCU\Software\Microsoft\Windows\CurrentVersion\Run (solo usuario actual, sin admin).</summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FocusTimer";

    private static string Command => "\"" + (Environment.ProcessPath ?? Application.ExecutablePath) + "\"";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue(ValueName, Command);
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or SecurityException or IOException)
        {
            return false;
        }
    }

    /// <summary>Si el .exe se movió de carpeta, actualiza la ruta guardada.</summary>
    public static void RefreshPathIfEnabled()
    {
        if (!IsEnabled()) return;
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        if (key?.GetValue(ValueName) as string != Command) SetEnabled(true);
    }
}
