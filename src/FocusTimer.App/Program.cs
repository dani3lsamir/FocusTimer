using FocusTimer.App.Native;
using FocusTimer.App.UI;
using FocusTimer.Core;

namespace FocusTimer.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Una sola instancia: si ya está abierta, se trae al frente y se sale.
        using var mutex = new Mutex(initiallyOwned: true, @"Local\FocusTimer.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            Win32.AllowSetForegroundWindow(-1); // ASFW_ANY: deja que la instancia abierta pase al frente
            Win32.PostMessage(Win32.HWND_BROADCAST, Win32.ShowWindowMessage, 0, 0);
            return;
        }

        // Antes de crear cualquier ventana: identidad para la barra de tareas y las notificaciones.
        Win32.SetCurrentProcessExplicitAppUserModelID(Toast.AppId);

        var paths = DataPaths.Default();
        paths.EnsureExists();

        ApplicationConfiguration.Initialize();
#pragma warning disable WFO5001 // modo oscuro de controles comunes (barras de desplazamiento, listas)
        Application.SetColorMode(SystemColorMode.Dark);
#pragma warning restore WFO5001
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Report(paths, e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(paths, e.ExceptionObject as Exception);

        Application.Run(new MainForm(paths));
    }

    /// <summary>Errores inesperados: solo a un archivo local (error.log). Nunca se envía nada.</summary>
    private static void Report(DataPaths paths, Exception? ex)
    {
        try
        {
            File.AppendAllText(Path.Combine(paths.Root, "error.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n");
            MessageBox.Show("Something went wrong. Details are in error.log, in the data folder.\n\n" + ex?.Message,
                "FocusTimer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception) { }
    }
}
