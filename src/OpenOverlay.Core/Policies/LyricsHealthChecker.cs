using OpenOverlay.Core.Models;

namespace OpenOverlay.Core.Policies;

public sealed record LyricsValidationResult(bool IsValid, bool IsSynchronized, string Outcome);

public static class LyricsHealthChecker
{
    public static LyricsValidationResult Validate(TrackSnapshot track, LyricsSnapshot? lyrics)
    {
        if (lyrics is null) return new(false, false, "not-found");
        if (!string.Equals(track.Id, lyrics.TrackId, StringComparison.Ordinal)) return new(false, false, "invalid");
        if (lyrics.Lines.Count == 0 || lyrics.Lines.Any(line => string.IsNullOrWhiteSpace(line.Text)))
            return new(false, false, "invalid");

        if (string.Equals(lyrics.SyncLevel, "none", StringComparison.OrdinalIgnoreCase))
            return new(true, false, "plain");

        if (!new[] { "line", "word", "syllable" }.Contains(lyrics.SyncLevel, StringComparer.OrdinalIgnoreCase))
            return new(false, false, "invalid");

        var lastStartMs = -1;
        var hasTimestampProgress = false;
        foreach (var line in lyrics.Lines)
        {
            if (line.StartMs < 0 || (track.DurationMs > 0 && line.StartMs > track.DurationMs + 15_000) || line.StartMs < lastStartMs)
                return new(false, false, "invalid");
            if (line.StartMs > 0) hasTimestampProgress = true;
            lastStartMs = line.StartMs;
        }

        if (lyrics.Lines.Count > 1 && !hasTimestampProgress)
            return new(false, false, "invalid");

        return new(true, true, "synced");
    }
}
