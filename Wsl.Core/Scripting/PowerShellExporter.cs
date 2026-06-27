namespace Wsl.Core.Scripting;

/// <summary>
/// Generates copy-pasteable wsl.exe command lines that mirror exactly what the
/// app's services run. Used for the "Copy PowerShell" / command-preview feature.
/// Argument values containing whitespace (or empty) are double-quoted.
/// </summary>
public sealed class PowerShellExporter : IPowerShellExporter
{
    public string Export(string name, string outPath, ExportFormat fmt) => string.Join("\r\n", new[]
    {
        "$running = @(wsl.exe --list --running --quiet | ForEach-Object { $_.Trim() } | Where-Object { $_ })",
        "wsl.exe --shutdown",
        "try {",
        $"    wsl.exe --export {Q(name)} {Q(outPath)} --format {FormatFlag(fmt)}",
        "}",
        "finally {",
        "    foreach ($d in $running) { wsl.exe -d $d -- true }",
        "}",
    });

    public string Restore(string name, string installDir, string archivePath, ExportFormat sourceFmt, int version) =>
        sourceFmt == ExportFormat.Vhd
            ? $"wsl.exe --import {Q(name)} {Q(installDir)} {Q(archivePath)} --vhd --version {version}"
            : $"wsl.exe --import {Q(name)} {Q(installDir)} {Q(archivePath)} --version {version}";

    public string Install(string name) => $"wsl.exe --install -d {Q(name)} --no-launch";

    public string Start(string name) => $"wsl.exe -d {Q(name)} -- true";
    public string Terminate(string name) => $"wsl.exe --terminate {Q(name)}";
    public string SetDefault(string name) => $"wsl.exe --set-default {Q(name)}";
    public string SetVersion(string name, int version) => $"wsl.exe --set-version {Q(name)} {version}";
    public string Unregister(string name) => $"wsl.exe --unregister {Q(name)}";
    public string List() => "wsl.exe --list --verbose";

    public string Optimize(string name) =>
        $"wsl.exe --terminate {Q(name)}\r\nwsl.exe --manage {Q(name)} --set-sparse true";

    public string Shutdown() => "wsl.exe --shutdown";

    /// <summary>Mirrors the launch-with-options dialog: the exact args TerminalLauncher passes to wsl.exe.</summary>
    public string Launch(string name, LaunchOptions options) =>
        "wsl.exe " + string.Join(" ", LaunchCommandBuilder.Build(name, options).Select(Q));

    /// <summary>
    /// The full Setup sequence mirroring the elevated broker: enable both Windows features,
    /// update the kernel, set WSL 2 as default. Must run from an elevated (admin) shell.
    /// </summary>
    public string EnableFeatures(bool preRelease = false) => string.Join("\r\n", new[]
    {
        "dism.exe /online /enable-feature /featurename:VirtualMachinePlatform /all /norestart",
        "dism.exe /online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux /all /norestart",
        preRelease ? "wsl.exe --update --pre-release" : "wsl.exe --update",
        "wsl.exe --set-default-version 2",
    });

    private static string FormatFlag(ExportFormat fmt) => fmt switch
    {
        ExportFormat.Tar => "tar",
        ExportFormat.TarGz => "tar.gz",
        ExportFormat.Vhd => "vhd",
        _ => throw new ArgumentOutOfRangeException(nameof(fmt))
    };

    /// <summary>Quote an argument value if it is empty or contains whitespace.</summary>
    private static string Q(string s) =>
        string.IsNullOrEmpty(s) || s.Any(char.IsWhiteSpace) ? $"\"{s}\"" : s;
}
