using OpenOverlay.Core.Models;

namespace OpenOverlay.Core.Abstractions;

public interface ISpotifyClient
{
    bool IsAuthenticated { get; }
    Task ConnectAsync(string clientId, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task<PlaybackSnapshot> GetPlaybackAsync(CancellationToken cancellationToken = default);
    Task ControlPlaybackAsync(string action, CancellationToken cancellationToken = default);
    Task SeekAsync(int positionMs, CancellationToken cancellationToken = default);
    Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default);
}
