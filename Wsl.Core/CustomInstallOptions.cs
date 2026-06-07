namespace Wsl.Core;

/// <summary>Options for an advanced `wsl --install` — custom name/location, local image file,
/// WSL version and store-bypass download. <see cref="Distro"/> and <see cref="FromFile"/>
/// are mutually exclusive.</summary>
public class CustomInstallOptions
{
    public string? Distro { get; set; }       // catalog name
    public string? FromFile { get; set; }     // local .wsl/.tar file — mutually exclusive with Distro
    public string? Name { get; set; }
    public string? Location { get; set; }
    public int? Version { get; set; }
    public bool WebDownload { get; set; }
}
