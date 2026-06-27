using Microsoft.Win32;
namespace Wsl.Core.Monitoring;

/// <summary>Sums each distro's ext4.vhdx size from the Lxss registry BasePath entries.</summary>
public sealed class RegistryVhdxSizeProbe : IVhdxSizeProbe
{
    public long TotalBytes()
    {
        long total = 0;
        using var lxss = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Lxss");
        if (lxss is null) return 0;
        foreach (var sub in lxss.GetSubKeyNames())
        {
            using var k = lxss.OpenSubKey(sub);
            if (k?.GetValue("BasePath") is not string bp) continue;
            var vhdx = Path.Combine(bp, "ext4.vhdx");
            if (File.Exists(vhdx)) total += new FileInfo(vhdx).Length;
        }
        return total;
    }
}
