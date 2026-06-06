namespace Wsl.Core.Scripting;

/// <summary>Produces wsl.exe command lines mirroring the app's operations.</summary>
public interface IPowerShellExporter
{
    string Export(string name, string outPath, ExportFormat fmt);
    string Restore(string name, string installDir, string archivePath, ExportFormat sourceFmt, int version);
    string Install(string name);
    string Start(string name);
    string Terminate(string name);
    string SetDefault(string name);
    string SetVersion(string name, int version);
    string Unregister(string name);
    string List();
    string Optimize(string name);
    string Shutdown();
    string EnableFeatures();
}
