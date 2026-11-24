using System.Runtime.InteropServices;

namespace FocusTimer.App.Native;

/// <summary>P/Invoke mínimos (todos locales al PC; nada de red).</summary>
internal static partial class Win32
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    private const uint FLASHW_STOP = 0, FLASHW_ALL = 3, FLASHW_TIMERNOFG = 12;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19, DWMWA_USE_IMMERSIVE_DARK_MODE = 20,
        DWMWA_BORDER_COLOR = 34, DWMWA_CAPTION_COLOR = 35;
    public static readonly IntPtr HWND_BROADCAST = new(0xffff);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlashWindowEx(ref FLASHWINFO pwfi);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessage(string name);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllowSetForegroundWindow(int processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool MessageBeep(uint type);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SetCurrentProcessExplicitAppUserModelID(string appId);

    [LibraryImport("uxtheme.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);

    /// <summary>Hace parpadear el botón de la barra de tareas hasta que la ventana pase al frente (como WeChat).</summary>
    public static void FlashUntilFocused(IntPtr hwnd)
    {
        var fi = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(), hwnd = hwnd,
            dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG, uCount = uint.MaxValue,
        };
        FlashWindowEx(ref fi);
    }

    public static void StopFlash(IntPtr hwnd)
    {
        var fi = new FLASHWINFO { cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(), hwnd = hwnd, dwFlags = FLASHW_STOP };
        FlashWindowEx(ref fi);
    }

    /// <summary>Barra de título oscura (Win10 20H1+/Win11) y negra en Win11.</summary>
    public static void UseDarkTitleBar(IntPtr hwnd)
    {
        int on = 1;
        if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, sizeof(int));
        int black = 0x000000;          // COLORREF
        int border = 0x00202020;
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref black, sizeof(int));  // solo Win11; se ignora si falla
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
    }
}
