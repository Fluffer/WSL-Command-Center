using System.Diagnostics;

namespace Wsl.App.Services;

/// <summary>
/// Opens an interactive wsl.exe session in a new console window.
/// App-side by design: Wsl.Core never starts processes directly (IProcessRunner only),
/// but an interactive console needs UseShellExecute, which IProcessRunner does not model.
/// </summary>
public static class TerminalLauncher
{
    public static void Launch(string[] wslArgs)
    {
        var psi = new ProcessStartInfo("wsl.exe")
        {
            UseShellExecute = true, // new console window
        };
        foreach (var a in wslArgs) psi.ArgumentList.Add(a);
        Process.Start(psi);
    }
}
