namespace Wsl.Core;

public enum DistroState { Running, Stopped, Installing, Unknown }

public record Distro(string Name, DistroState State, int Version, bool IsDefault);
