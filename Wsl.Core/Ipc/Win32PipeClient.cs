using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Wsl.Core.Ipc;

internal static class Win32PipeClient
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafeHandle Pipe, out uint ServerProcessId);

    public static int GetServerPid(SafeHandle pipeHandle)
        => GetNamedPipeServerProcessId(pipeHandle, out var pid) ? (int)pid : -1;
}
