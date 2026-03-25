using System.IO;

namespace NetworkAdaptersToggle.Services;

public sealed class IniSettingsService
{
    private const string SectionHeader = "[SelectedAdapters]";
    private readonly string _filePath;

    public IniSettingsService()
    {
        _filePath = GetSettingsPath();
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

        try
        {
            File.WriteAllLines(_filePath, lines);
        }
        catch (UnauthorizedAccessException)
        {
            // Fallback to AppData if writing next to exe fails (e.g., MSIX)
            var fallback = GetFallbackPath();
            File.WriteAllLines(fallback, lines);
        }
    }

    private static string GetSettingsPath()
    {
        var exeDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(exeDir, "settings.ini");

        // Check if we can write next to the exe
        try
        {
            using var _ = File.Open(candidate, FileMode.OpenOrCreate, FileAccess.ReadWrite);
            return candidate;
        }
        catch
        {
            return GetFallbackPath();
        }
    }

    private static string GetFallbackPath()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetworkAdaptersToggle");
        Directory.CreateDirectory(appData);
        return Path.Combine(appData, "settings.ini");
    }
}
