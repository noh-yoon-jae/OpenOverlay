using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenOverlay.Core.Models;

namespace OpenOverlay.Infrastructure.Lyrics;

public sealed class CachedLyricsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _directory;

    public CachedLyricsStore(string? directory = null) => _directory = directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenOverlay", "LyricsCache");

    public LyricsSnapshot? Read(string providerId, string trackId)
    {
        var path = GetPath(providerId, trackId);
        try
        {
            if (!File.Exists(path)) return null;
            var entry = JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(path), JsonOptions);
            if (entry is null || entry.ExpiresAtUtc <= DateTimeOffset.UtcNow) return null;
            return entry.Lyrics;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Write(string providerId, string trackId, LyricsSnapshot lyrics)
    {
        Directory.CreateDirectory(_directory);
        var path = GetPath(providerId, trackId);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new CacheEntry(DateTimeOffset.UtcNow.AddDays(1), lyrics), JsonOptions));
        File.Move(temporaryPath, path, overwrite: true);
    }

    public void Delete(string providerId, string trackId)
    {
        var path = GetPath(providerId, trackId);
        if (File.Exists(path)) File.Delete(path);
    }

    private string GetPath(string providerId, string trackId) => Path.Combine(_directory, Hash(providerId) + "-" + Hash(trackId) + ".json");
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private sealed record CacheEntry(DateTimeOffset ExpiresAtUtc, LyricsSnapshot Lyrics);
}
