using System.Text.Json;
using OpenOverlay.Core.Models;

namespace OpenOverlay.Infrastructure.Lyrics;

public sealed class LyricsHealthStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly Dictionary<string, Counters> _providers;
    private readonly object _gate = new();

    public LyricsHealthStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenOverlay", "lyrics-health.json");
        _providers = Load();
    }

    public void Record(string providerId, string outcome)
    {
        lock (_gate)
        {
            if (!_providers.TryGetValue(providerId, out var counters))
            {
                counters = new Counters();
                _providers[providerId] = counters;
            }

            counters.Attempts++;
            switch (outcome)
            {
                case "synced": counters.SyncedResults++; break;
                case "plain": counters.PlainResults++; break;
                case "not-found": counters.NotFound++; break;
                case "invalid": counters.InvalidResults++; break;
                default: counters.Failures++; outcome = "error"; break;
            }
            counters.LastOutcome = outcome;
            counters.LastCheckedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Save();
        }
    }

    public LyricsHealthSnapshot Snapshot(IEnumerable<(string Id, string Name)> providers)
    {
        lock (_gate)
        {
            return new LyricsHealthSnapshot(providers.Select(provider =>
            {
                _providers.TryGetValue(provider.Id, out var counters);
                counters ??= new Counters();
                return new LyricsProviderHealth(provider.Id, provider.Name, counters.Attempts, counters.SyncedResults, counters.PlainResults,
                    counters.NotFound, counters.InvalidResults, counters.Failures, counters.LastOutcome, counters.LastCheckedAtMs);
            }).ToArray());
        }
    }

    private Dictionary<string, Counters> Load()
    {
        try
        {
            if (!File.Exists(_path)) return new(StringComparer.OrdinalIgnoreCase);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, Counters>>(File.ReadAllText(_path), JsonOptions);
            return loaded is null
                ? new(StringComparer.OrdinalIgnoreCase)
                : new(loaded.Where(pair => pair.Key is "lrclib" or "spotify").ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporaryPath = _path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_providers, JsonOptions));
            File.Move(temporaryPath, _path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Health statistics are best-effort and must never interrupt lyric loading.
        }
    }

    private sealed class Counters
    {
        public Counters() { }
        public int Attempts { get; set; }
        public int SyncedResults { get; set; }
        public int PlainResults { get; set; }
        public int NotFound { get; set; }
        public int InvalidResults { get; set; }
        public int Failures { get; set; }
        public string LastOutcome { get; set; } = "never";
        public long? LastCheckedAtMs { get; set; }
    }
}
