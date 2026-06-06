namespace Wsl.Core;

/// <summary>section -> key -> value, preserving insertion order.</summary>
public static class IniParser
{
    public static Dictionary<string, Dictionary<string, string>> Parse(string text)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var current = "";
        result[current] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                current = line[1..^1].Trim();
                if (!result.ContainsKey(current))
                    result[current] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            result[current][key] = value;
        }
        return result;
    }

    public static string Write(Dictionary<string, Dictionary<string, string>> ini)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (section, kv) in ini)
        {
            if (kv.Count == 0) continue;
            if (section.Length > 0) sb.AppendLine($"[{section}]");
            foreach (var (k, v) in kv) sb.AppendLine($"{k}={v}");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
