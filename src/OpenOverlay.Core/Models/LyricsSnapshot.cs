namespace OpenOverlay.Core.Models;

public sealed record LyricLineSnapshot(string Text, int StartMs, int? EndMs = null);

public sealed record LyricsSnapshot(
    string TrackId,
    string SourceId,
    string SourceName,
    string SyncLevel,
    IReadOnlyList<LyricLineSnapshot> Lines);
