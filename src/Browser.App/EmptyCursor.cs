using System.Runtime.InteropServices;
using Microsoft.UI.Input;

namespace Browser.App;

internal static class EmptyCursor
{
    public static InputCursor Create()
    {
        var mask = new byte[128];
        Array.Fill(mask, (byte)255);
        var handle = CreateCursor(0, 0, 0, 32, 32, mask, new byte[128]);
        if (handle == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        nint result = 0;
        try
        {
            InputCursor.As<IInputCursorStaticsInterop>().CreateFromHCursor(handle, out result);
            return WinRT.MarshalInterface<InputCursor>.FromAbi(result);
        }
        finally { if (result != 0) Marshal.Release(result); DestroyCursor(handle); }
    }
    [ComImport, Guid("ac6f5065-90c4-46ce-beb7-05e138e54117"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInputCursorStaticsInterop
    {
        void GetIids(out uint count, out nint ids);
        void GetRuntimeClassName(out nint name);
        void GetTrustLevel(out int level);
        void CreateFromHCursor(nint cursor, out nint result);
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern nint CreateCursor(nint instance, int x, int y, int width, int height, byte[] andMask, byte[] xorMask);
    [DllImport("user32.dll")] private static extern bool DestroyCursor(nint cursor);
}
