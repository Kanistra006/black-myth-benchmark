using System.Text;
using System.Text.RegularExpressions;

namespace WukongBenchRunner.Settings;

/// <summary>
/// The game keeps every option shown in its settings menu in a single
/// GameUserSettings.ini value: <c>UISettingData=(("Key", "Value"),("Key2", "Value2"))</c>.
/// This type parses and re-serialises that value while preserving entry order.
/// </summary>
public sealed partial class UiSettingData
{
    private readonly List<KeyValuePair<string, string>> _entries;

    private UiSettingData(List<KeyValuePair<string, string>> entries) => _entries = entries;

    public IReadOnlyList<KeyValuePair<string, string>> Entries => _entries;

    public static UiSettingData Parse(string? raw)
    {
        var entries = new List<KeyValuePair<string, string>>();
        if (!string.IsNullOrWhiteSpace(raw))
        {
            foreach (Match m in EntryRegex().Matches(raw))
                entries.Add(new(m.Groups[1].Value, m.Groups[2].Value));
        }
        return new UiSettingData(entries);
    }

    public string? this[string key]
    {
        get => _entries.FirstOrDefault(e => e.Key == key).Value;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var i = _entries.FindIndex(e => e.Key == key);
            if (i >= 0) _entries[i] = new(key, value);
            else _entries.Add(new(key, value));
        }
    }

    public override string ToString()
    {
        var sb = new StringBuilder("(");
        for (var i = 0; i < _entries.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append($"(\"{_entries[i].Key}\", \"{_entries[i].Value}\")");
        }
        return sb.Append(')').ToString();
    }

    [GeneratedRegex("\\(\"([^\"]*)\",\\s*\"([^\"]*)\"\\)")]
    private static partial Regex EntryRegex();
}
