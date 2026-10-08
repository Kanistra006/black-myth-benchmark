using System.Text;

namespace WukongBenchRunner.Settings;

/// <summary>
/// Minimal line-preserving INI editor for Unreal Engine config files.
/// Unknown lines, comments and key order are kept intact so the game sees
/// exactly the file it wrote, except for the keys we change.
/// </summary>
public sealed class IniDocument
{
    private readonly List<string> _lines;

    private IniDocument(List<string> lines) => _lines = lines;

    public static IniDocument Parse(string text) =>
        new(text.Replace("\r\n", "\n").Split('\n').ToList());

    public static IniDocument Load(string path) => Parse(File.ReadAllText(path));

    public void Save(string path) => File.WriteAllText(path, ToString(), new UTF8Encoding(false));

    public override string ToString() => string.Join("\r\n", _lines);

    public string? Get(string section, string key)
    {
        var idx = FindKey(section, key);
        return idx < 0 ? null : _lines[idx][(_lines[idx].IndexOf('=') + 1)..];
    }

    public void Set(string section, string key, string value)
    {
        var idx = FindKey(section, key);
        if (idx >= 0)
        {
            _lines[idx] = $"{key}={value}";
            return;
        }

        var (start, end) = FindSection(section);
        if (start < 0)
        {
            if (_lines.Count > 0 && _lines[^1].Length != 0) _lines.Add("");
            _lines.Add($"[{section}]");
            _lines.Add($"{key}={value}");
            return;
        }

        // Insert after the last non-empty line of the section.
        var insertAt = end;
        while (insertAt > start + 1 && string.IsNullOrWhiteSpace(_lines[insertAt - 1])) insertAt--;
        _lines.Insert(insertAt, $"{key}={value}");
    }

    private int FindKey(string section, string key)
    {
        var (start, end) = FindSection(section);
        if (start < 0) return -1;
        for (var i = start + 1; i < end; i++)
        {
            var line = _lines[i];
            var eq = line.IndexOf('=');
            if (eq > 0 && line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    /// <returns>Index of the section header and the index one past its last line, or (-1, -1).</returns>
    private (int Start, int End) FindSection(string section)
    {
        var header = $"[{section}]";
        var start = _lines.FindIndex(l => l.Trim().Equals(header, StringComparison.OrdinalIgnoreCase));
        if (start < 0) return (-1, -1);
        var end = start + 1;
        while (end < _lines.Count && !_lines[end].TrimStart().StartsWith('[')) end++;
        return (start, end);
    }
}
