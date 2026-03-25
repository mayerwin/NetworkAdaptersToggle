using System.IO;

namespace NetworkAdaptersToggle.Services;

public sealed class IniSettingsService
{
    private const string SectionHeader = "[SelectedAdapters]";
    private readonly string _filePath;

    public IniSettingsService()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetworkAdaptersToggle");
        Directory.CreateDirectory(appData);
        _filePath = Path.Combine(appData, "settings.ini");
    }

    public HashSet<int> LoadSelectedIndexes()
    {
        var result = new HashSet<int>();

        if (!File.Exists(_filePath))
            return result;

        var inSection = false;
        foreach (var line in File.ReadAllLines(_filePath))
        {
            var trimmed = line.Trim();

            if (trimmed.Equals(SectionHeader, StringComparison.OrdinalIgnoreCase))
            {
                inSection = true;
                continue;
            }

            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                inSection = false;
                continue;
            }

            if (!inSection || trimmed.StartsWith(';') || string.IsNullOrWhiteSpace(trimmed))
                continue;

            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex > 0)
            {
                var key = trimmed[..eqIndex].Trim();
                if (int.TryParse(key, out var interfaceIndex))
                    result.Add(interfaceIndex);
            }
        }

        return result;
    }

    public void SaveSelectedIndexes(IEnumerable<int> indexes)
    {
        var lines = new List<string> { SectionHeader };
        foreach (var index in indexes.Order())
        {
            lines.Add($"{index}=1");
        }

        File.WriteAllLines(_filePath, lines);
    }
}
