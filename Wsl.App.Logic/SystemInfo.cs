using System.Runtime.InteropServices;

namespace Wsl.App.Logic;

/// <summary>Reads host hardware totals so the UI can show what WSL2 defaults to when unconfigured.</summary>
internal static class SystemInfo
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    /// <summary>Total physical RAM in GiB, or 0 if it can't be read.</summary>
    public static double TotalPhysicalGiB()
    {
        try
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref m))
                return Math.Round(m.ullTotalPhys / 1024d / 1024d / 1024d, 1);
        }
        catch { /* fall through */ }
        return 0;
    }

    public static int LogicalProcessors => Environment.ProcessorCount;
}
