using System.Runtime.InteropServices;
using System.Text;

namespace SoundpadTranslator;

/// <summary>
/// A key id is the hardware scan code plus prefix flags: bit 8 = E0 (extended), bit 9 = E1 (Pause).
/// Scan codes, unlike virtual keys, don't depend on the keyboard layout or NumLock.
/// </summary>
static class KeyNames
{
    const int E0 = 0x100;
    const int E1 = 0x200;

    public static int ToKeyId(ushort code, ushort state) =>
        code
        | ((state & Interception.KeyE0) != 0 ? E0 : 0)
        | ((state & Interception.KeyE1) != 0 ? E1 : 0);

    public static string Describe(int key)
    {
        int scanCode = key & 0xFF;
        if ((key & E1) != 0)
            return scanCode == 0x1D ? "Pause" : $"E1 {scanCode:X2}";

        int lParam = (scanCode << 16) | ((key & E0) != 0 ? 1 << 24 : 0);
        var sb = new StringBuilder(64);
        int length = GetKeyNameTextW(lParam, sb, sb.Capacity);
        return length > 0 ? sb.ToString() : $"Scan {((key & E0) != 0 ? "E0 " : "")}{scanCode:X2}";
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetKeyNameTextW(int lParam, StringBuilder buffer, int size);
}
