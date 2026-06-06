using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Wsl.Broker;

internal static class Win32Pipe
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(SafeHandle Pipe, out uint ClientProcessId);

    public static int GetClientPid(SafeHandle pipeHandle)
        => GetNamedPipeClientProcessId(pipeHandle, out var pid) ? (int)pid : -1;
}
