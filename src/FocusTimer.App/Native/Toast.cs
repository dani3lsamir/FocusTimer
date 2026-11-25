using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace FocusTimer.App.Native;

/// <summary>
/// Notificaciones "toast" reales de Windows para un .exe sin empaquetar, SIN paquetes NuGet ni la proyección
/// WinRT de 25 MB: se llama a las API WinRT de combase.dll directamente por su vtable (~100 líneas).
/// El AppUserModelID se registra en HKCU (sin admin, sin acceso directo). Si algo falla, se devuelve false
/// y la app usa el parpadeo + un sonido del sistema.
/// </summary>
internal static unsafe class Toast
{
    public const string AppId = "FocusTimer.App";

    private static readonly Guid IID_IToastNotificationManagerStatics = new("50AC103F-D235-4598-BBEF-98FE4D1A3AD4");
    private static readonly Guid IID_IToastNotificationFactory = new("04124B20-82C6-4229-B109-FD9ED4662B53");
    private static readonly Guid IID_IXmlDocumentIO = new("6CD0E74E-EE65-4489-9EBF-CA43E87BA637");
    private static readonly Guid IID_IXmlDocument = new("F7F3A506-1E87-42D6-BCFB-B8C809FA5494");

    [DllImport("combase.dll")]
    private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string src, uint length, out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr classId, in Guid iid, out IntPtr factory);

    [DllImport("combase.dll")]
    private static extern int RoActivateInstance(IntPtr classId, out IntPtr instance);

    /// <summary>Registra el AppUserModelID (nombre e icono que muestra Windows en el toast). Solo HKCU.</summary>
    public static void Register(string displayName, string? iconPath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + AppId);
            key.SetValue("DisplayName", displayName);
            if (iconPath is not null) key.SetValue("IconUri", iconPath);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or SecurityException or IOException) { }
    }

    /// <summary>Muestra el toast en un hilo MTA propio para no bloquear la UI. onFailure se llama en ese hilo.</summary>
    public static void ShowAsync(string title, string body, Action onFailure)
    {
        var t = new Thread(() =>
        {
            bool ok;
            try { ok = Show(title, body); } catch (Exception) { ok = false; }
            if (!ok) onFailure();
        }) { IsBackground = true, Name = "toast" };
        t.SetApartmentState(ApartmentState.MTA);
        t.Start();
    }

    private static bool Show(string title, string body)
    {
        string xml =
            "<toast><visual><binding template=\"ToastGeneric\">" +
            $"<text>{SecurityElement.Escape(title)}</text><text>{SecurityElement.Escape(body)}</text>" +
            "</binding></visual><audio src=\"ms-winsoundevent:Notification.Default\"/></toast>";

        IntPtr statics = 0, notifier = 0, xmlInsp = 0, xmlIo = 0, xmlDoc = 0, factory = 0, toast = 0;
        try
        {
            using (var cls = new HStr("Windows.UI.Notifications.ToastNotificationManager"))
                if (!Ok(cls.Hr) || !Ok(RoGetActivationFactory(cls.H, IID_IToastNotificationManagerStatics, out statics))) return false;
            // IToastNotificationManagerStatics::CreateToastNotifierWithId (slot 7)
            using (var id = new HStr(AppId))
                if (!Ok(id.Hr) || !Ok(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Slot(statics, 7))(statics, id.H, &notifier))) return false;

            using (var cls = new HStr("Windows.Data.Xml.Dom.XmlDocument"))
                if (!Ok(cls.Hr) || !Ok(RoActivateInstance(cls.H, out xmlInsp))) return false;
            if (!Ok(Marshal.QueryInterface(xmlInsp, IID_IXmlDocumentIO, out xmlIo))) return false;
            // IXmlDocumentIO::LoadXml (slot 6)
            using (var text = new HStr(xml))
                if (!Ok(text.Hr) || !Ok(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Slot(xmlIo, 6))(xmlIo, text.H))) return false;
            if (!Ok(Marshal.QueryInterface(xmlInsp, IID_IXmlDocument, out xmlDoc))) return false;

            using (var cls = new HStr("Windows.UI.Notifications.ToastNotification"))
                if (!Ok(cls.Hr) || !Ok(RoGetActivationFactory(cls.H, IID_IToastNotificationFactory, out factory))) return false;
            // IToastNotificationFactory::CreateToastNotification (slot 6)
            if (!Ok(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Slot(factory, 6))(factory, xmlDoc, &toast))) return false;
            // IToastNotifier::Show (slot 6)
            return Ok(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Slot(notifier, 6))(notifier, toast));
        }
        finally
        {
            foreach (var p in new[] { toast, factory, xmlDoc, xmlIo, xmlInsp, notifier, statics })
                if (p != 0) Marshal.Release(p);
        }
    }

    private static IntPtr Slot(IntPtr obj, int index) => (*(IntPtr**)obj)[index];

    private static bool Ok(int hr) => hr >= 0;

    /// <summary>HSTRING de WinRT que se libera al salir del bloque.</summary>
    private readonly ref struct HStr
    {
        public readonly IntPtr H;
        public readonly int Hr;
        public HStr(string s) => Hr = WindowsCreateString(s, (uint)s.Length, out H);
        public void Dispose() { if (H != 0) WindowsDeleteString(H); }
    }
}
