using System.Runtime.InteropServices;

namespace Browser.App;

internal static class SystemMemory
{
    public static (long Total, long Available, long Commit) Read()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref status) ? ((long)(status.TotalPhysical / (1024 * 1024)),
            (long)(status.AvailablePhysical / (1024 * 1024)), (long)(status.AvailablePageFile / (1024 * 1024))) : (0, 0, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
