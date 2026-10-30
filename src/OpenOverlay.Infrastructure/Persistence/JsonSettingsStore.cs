using System.Text.Json;
using OpenOverlay.Core.Abstractions;
using OpenOverlay.Core.Models;

namespace OpenOverlay.Infrastructure.Persistence;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenOverlay",
        "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AppSettings();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions) ?? new AppSettings();
            var savedProviders = settings.ProviderEnabled ?? new(StringComparer.OrdinalIgnoreCase);
            settings.ProviderEnabled = AppSettings.CreateDefaultProviderSettings();
            foreach (var providerId in settings.ProviderEnabled.Keys.ToArray())
                if (savedProviders.TryGetValue(providerId, out var enabled)) settings.ProviderEnabled[providerId] = enabled;
            settings.LyricsOffsetsMs ??= new(StringComparer.OrdinalIgnoreCase);
            settings.LyricsProviderOrder = NormalizeProviderOrder(settings.LyricsProviderOrder);
            return settings;
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, _path, overwrite: true);
    }

    public static List<string> NormalizeProviderOrder(IEnumerable<string>? order)
    {
        var known = new[] { "lrclib", "spotify" };
        var normalized = (order ?? []).Where(id => known.Contains(id, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        normalized.AddRange(known.Where(id => !normalized.Contains(id, StringComparer.OrdinalIgnoreCase)));
        return normalized;
    }
}
