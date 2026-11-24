using System.Runtime.InteropServices;

namespace FocusTimer.App.Native;

public enum TaskbarState { None, Focus, Break, Paused }

/// <summary>
/// Progreso en el botón de la barra de tareas vía ITaskbarList3 (llamadas directas a la vtable COM,
/// sin ensamblados de interop). Windows solo tiene tres colores: verde (normal), amarillo (paused) y rojo (error).
/// Enfoque = verde, descanso = amarillo, pausa = rojo.
/// </summary>
internal sealed unsafe class TaskbarProgress : IDisposable
{
    private static readonly Guid CLSID_TaskbarList = new("56FDF344-FD6D-11d0-958A-006097C9A090");
    private static readonly Guid IID_ITaskbarList3 = new("ea1afb91-9e28-4b86-90e9-9e9f8a5eee7c");
    private const int TBPF_NOPROGRESS = 0, TBPF_NORMAL = 2, TBPF_ERROR = 4, TBPF_PAUSED = 8;
    private const uint CLSCTX_INPROC_SERVER = 1;
    private const ulong Scale = 10_000;

    private IntPtr _ptr;

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(in Guid clsid, IntPtr outer, uint ctx, in Guid iid, out IntPtr ppv);

    /// <summary>Se llama al recibir TaskbarButtonCreated (o al crear la ventana).</summary>
    public void Init()
    {
        if (_ptr != IntPtr.Zero) return;
        if (CoCreateInstance(CLSID_TaskbarList, IntPtr.Zero, CLSCTX_INPROC_SERVER, IID_ITaskbarList3, out var p) != 0) return;
        var vtbl = *(IntPtr**)p;
        var hrInit = (delegate* unmanaged[Stdcall]<IntPtr, int>)vtbl[3];
        if (hrInit(p) != 0) { Marshal.Release(p); return; }
        _ptr = p;
    }

    public void Set(IntPtr hwnd, TaskbarState state, double fraction)
    {
        if (_ptr == IntPtr.Zero) Init();
        if (_ptr == IntPtr.Zero || hwnd == IntPtr.Zero) return;
        var vtbl = *(IntPtr**)_ptr;
        var setValue = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, ulong, ulong, int>)vtbl[9];
        var setState = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, int>)vtbl[10];
        int flag = state switch
        {
            TaskbarState.Focus => TBPF_NORMAL,
            TaskbarState.Break => TBPF_PAUSED,
            TaskbarState.Paused => TBPF_ERROR,
            _ => TBPF_NOPROGRESS,
        };
        setState(_ptr, hwnd, flag);
        if (flag != TBPF_NOPROGRESS)
            setValue(_ptr, hwnd, (ulong)(Math.Clamp(fraction, 0, 1) * Scale), Scale);
    }

    /// <summary>Tras reiniciarse Explorer hay que pedir de nuevo la interfaz.</summary>
    public void Reset()
    {
        Dispose();
        Init();
    }

    public void Dispose()
    {
        if (_ptr != IntPtr.Zero) { Marshal.Release(_ptr); _ptr = IntPtr.Zero; }
    }
}
