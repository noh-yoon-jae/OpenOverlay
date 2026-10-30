namespace OpenOverlay.Core.Models;

public sealed record TrackSnapshot(
    string Id,
    string Title,
    IReadOnlyList<string> Artists,
    string Album,
    string? AlbumArtUrl,
    int DurationMs);

public sealed record PlaybackSnapshot(
    string Status,
    bool IsConnected,
    bool IsPlaying,
    int PositionMs,
    long ObservedAtMs,
    TrackSnapshot? Track,
    bool SpotifyLyricsSessionConnected,
    string? Message = null,
    int? VolumePercent = null);
