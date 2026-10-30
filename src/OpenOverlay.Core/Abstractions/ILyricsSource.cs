using OpenOverlay.Core.Models;

namespace OpenOverlay.Core.Abstractions;

public interface ILyricsSource
{
    string Id { get; }
    string DisplayName { get; }
    Task<LyricsSnapshot?> GetLyricsAsync(TrackSnapshot track, string? spotifyWebSessionCookie = null, CancellationToken cancellationToken = default);
}
