using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using OpenOverlay.Core.Abstractions;
using OpenOverlay.Core.Models;
using OpenOverlay.Infrastructure.Spotify;

namespace OpenOverlay.Infrastructure.Lyrics;

/// <summary>
/// Experimental Spotify web-session source. Cookie egress is fixed to Spotify's lyrics host,
/// and redirects are rejected so a response cannot forward the cookie elsewhere.
/// </summary>
public sealed class SpotifyWebLyricsSource : ILyricsSource, IDisposable
{
    public const string LyricsHost = "spclient.wg.spotify.com";
    private static readonly Uri LyricsOrigin = new($"https://{LyricsHost}/");
    private readonly HttpClient _http;
    private readonly SpotifyWebAccessTokenClient _tokenClient = new();

    public SpotifyWebLyricsSource(HttpMessageHandler? handler = null)
    {
        handler ??= new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(12) };
    }

    public string Id => "spotify";
    public string DisplayName => "Spotify (experimental)";

    public async Task<LyricsSnapshot?> GetLyricsAsync(TrackSnapshot track, string? spotifyWebSessionCookie = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(spotifyWebSessionCookie))
            throw new InvalidOperationException("Spotify lyrics need an app-only Spotify web session. Enable the source in Settings first.");
        if (spotifyWebSessionCookie.Length > 4096 || spotifyWebSessionCookie.Any(character => char.IsControl(character) || character == ';'))
            throw new ArgumentException("The Spotify session cookie has an invalid format.", nameof(spotifyWebSessionCookie));
        if (string.IsNullOrWhiteSpace(track.Id) || track.Id.Length > 128 || track.Id.Any(character => !char.IsAsciiLetterOrDigit(character)))
            throw new ArgumentException("The Spotify track ID is not valid.", nameof(track));

        var uri = new Uri(LyricsOrigin, $"color-lyrics/v2/track/{Uri.EscapeDataString(track.Id)}?format=json&market=from_token");
        if (uri.Scheme != Uri.UriSchemeHttps || !string.Equals(uri.Host, LyricsHost, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Spotify lyrics request destination was rejected.");

        var accessToken = await _tokenClient.GetAccessTokenAsync(spotifyWebSessionCookie, cancellationToken: cancellationToken);
        using var response = await SendLyricsRequestAsync(uri, accessToken, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            response.Dispose();
            accessToken = await _tokenClient.GetAccessTokenAsync(spotifyWebSessionCookie, forceRefresh: true, cancellationToken);
            using var retryResponse = await SendLyricsRequestAsync(uri, accessToken, cancellationToken);
            return await ParseLyricsResponseAsync(track, retryResponse, cancellationToken);
        }
        return await ParseLyricsResponseAsync(track, response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendLyricsRequestAsync(Uri uri, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("app-platform", "WebPlayer");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Referrer = new Uri("https://open.spotify.com/");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private async Task<LyricsSnapshot?> ParseLyricsResponseAsync(TrackSnapshot track, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if ((int)response.StatusCode is >= 300 and < 400)
            throw new InvalidOperationException("Spotify lyrics endpoint redirected the request. OpenOverlay rejected the redirect.");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException("Spotify rejected the refreshed Web Player token for lyrics. The private lyrics endpoint may have changed or may not allow this account.");
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("Spotify is rate limiting lyrics requests. Try again later.");
        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("lyrics", out var lyrics) || !lyrics.TryGetProperty("lines", out var linesElement) || linesElement.ValueKind != JsonValueKind.Array)
            return null;

        var lines = new List<LyricLineSnapshot>();
        foreach (var line in linesElement.EnumerateArray())
        {
            var text = line.TryGetProperty("words", out var words) ? words.GetString() : null;
            var startMs = ReadMilliseconds(line, "startTimeMs");
            var endMs = ReadMilliseconds(line, "endTimeMs");
            if (!string.IsNullOrWhiteSpace(text)) lines.Add(new LyricLineSnapshot(text, startMs, endMs > 0 ? endMs : null));
        }
        if (lines.Count == 0) return null;
        var syncType = lyrics.TryGetProperty("syncType", out var sync) ? sync.GetString() : null;
        var syncLevel = string.Equals(syncType, "LINE_SYNCED", StringComparison.OrdinalIgnoreCase) ? "line" : "none";
        return new LyricsSnapshot(track.Id, Id, DisplayName, syncLevel, lines);
    }

    public void Dispose()
    {
        _http.Dispose();
        _tokenClient.Dispose();
    }

    public Task ClearCachedSessionTokenAsync(CancellationToken cancellationToken = default) => _tokenClient.ClearCachedTokenAsync(cancellationToken);

    private static int ReadMilliseconds(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number) ? number : 0;
    }
}
