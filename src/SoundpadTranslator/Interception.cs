using System.Runtime.InteropServices;
using System.Text;

namespace SoundpadTranslator;

/// <summary>Mirror of InterceptionStroke (sizeof(InterceptionMouseStroke) = 20 bytes), read as a key stroke.</summary>
[StructLayout(LayoutKind.Explicit, Size = 20)]
struct Stroke
{
    [FieldOffset(0)] public ushort Code;
    [FieldOffset(2)] public ushort State;
    [FieldOffset(4)] public uint Information;
}

/// <summary>P/Invoke bindings for oblitum/Interception (interception.h, v1.0.1).</summary>
static class Interception
{
    const string Dll = "interception.dll";

    public const int MaxKeyboard = 10;

    public const ushort KeyUp = 0x01;
    public const ushort KeyE0 = 0x02;
    public const ushort KeyE1 = 0x04;

    public const ushort FilterKeyAll = 0xFFFF;
    public const ushort FilterKeyNone = 0x0000;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int Predicate(int device);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr interception_create_context();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void interception_destroy_context(IntPtr context);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void interception_set_filter(IntPtr context, Predicate predicate, ushort filter);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int interception_wait_with_timeout(IntPtr context, uint milliseconds);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int interception_send(IntPtr context, int device, ref Stroke stroke, uint nstroke);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int interception_receive(IntPtr context, int device, ref Stroke stroke, uint nstroke);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    static extern uint interception_get_hardware_id(IntPtr context, int device, byte[] buffer, uint bufferSize);

    public static bool IsKeyboard(int device) => device >= 1 && device <= MaxKeyboard;

    /// <summary>First entry of the device's REG_MULTI_SZ hardware ID list, or null if no device is in that slot.</summary>
    public static string? GetHardwareId(IntPtr context, int device)
    {
        var buffer = new byte[1024];
        uint length = interception_get_hardware_id(context, device, buffer, (uint)buffer.Length);
        if (length == 0)
            return null;
        var text = Encoding.Unicode.GetString(buffer, 0, (int)Math.Min(length, (uint)buffer.Length));
        var first = text.Split('\0', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(first) ? null : first;
    }
}
