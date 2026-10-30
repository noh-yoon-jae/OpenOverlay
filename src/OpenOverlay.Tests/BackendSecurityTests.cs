using System.Net;
using System.Text.Json;
using OpenOverlay.Core.Models;
using OpenOverlay.Core.Policies;
using OpenOverlay.Infrastructure.Lyrics;
using OpenOverlay.Infrastructure.Spotify;
using Xunit;

namespace OpenOverlay.Tests;

public sealed class BackendSecurityTests
{
    [Fact]
    public void SpotifyLyrics_are_off_by_default_and_require_consent_and_session()
    {
        var settings = new AppSettings();
        Assert.False(settings.IsProviderEnabled("spotify"));
        Assert.False(LyricsProviderPolicy.CanUse("spotify", settings.IsProviderEnabled("spotify"), settings.SpotifyLyricsConsentAccepted, hasSpotifySession: true));
        Assert.False(LyricsProviderPolicy.CanUse("spotify", enabled: true, consentAccepted: false, hasSpotifySession: true));
        Assert.False(LyricsProviderPolicy.CanUse("spotify", enabled: true, consentAccepted: true, hasSpotifySession: false));
        Assert.True(LyricsProviderPolicy.CanUse("spotify", enabled: true, consentAccepted: true, hasSpotifySession: true));
    }

    [Fact]
    public void SpotifyLyrics_consent_denial_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => LyricsProviderPolicy.RequireSpotifyConsent(false));
        LyricsProviderPolicy.RequireSpotifyConsent(true);
    }

    [Fact]
    public void Pkce_state_requires_exact_match()
    {
        Assert.True(SpotifyPkceClient.StateMatches("state-123", "state-123"));
        Assert.False(SpotifyPkceClient.StateMatches("state-123", "state-124"));
        Assert.False(SpotifyPkceClient.StateMatches("state-123", "short"));
    }

    [Fact]
    public void Spotify_playback_response_maps_track_and_progress()
    {
        using var json = JsonDocument.Parse("""
        {"is_playing":true,"progress_ms":18500,"item":{"type":"track","id":"track123","name":"Title","duration_ms":241000,"artists":[{"name":"Artist"}],"album":{"name":"Album","images":[{"url":"https://i.scdn.co/image/test"}]}}}
        """);

        var snapshot = SpotifyPkceClient.MapPlayback(json.RootElement, isConnected: true, hasWebSession: false);

        Assert.Equal("playing", snapshot.Status);
        Assert.Equal(18500, snapshot.PositionMs);
        Assert.Equal("track123", snapshot.Track?.Id);
        Assert.Equal("Artist", Assert.Single(snapshot.Track!.Artists));
        Assert.Equal("https://i.scdn.co/image/test", snapshot.Track.AlbumArtUrl);
    }

    [Fact]
    public async Task Refresh_token_is_rotated_and_player_control_errors_are_reported()
    {
        var credentials = new FakeCredentialStore();
        credentials.Write("spotify-refresh-token", "refresh-1");
        var handler = new RoutingHandler(request =>
        {
            if (request.RequestUri!.Host == "accounts.spotify.com")
                return JsonResponse("""{"access_token":"access-1","refresh_token":"refresh-2","expires_in":3600}""");
            return new HttpResponseMessage(HttpStatusCode.Forbidden);
        });
        using var client = new SpotifyPkceClient(credentials, handler);
        client.SetClientId("client-id");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ControlPlaybackAsync("next"));

        Assert.Equal("refresh-2", credentials.Read("spotify-refresh-token"));
        Assert.Contains("Spotify rejected this action", error.Message);
        Assert.Equal("https://api.spotify.com/v1/me/player/next", handler.Requests.Last().RequestUri!.ToString());
    }

    [Fact]
    public void Lrc_parser_converts_fractional_timestamps_and_repeated_tags()
    {
        var lines = LyricifyLrclibSource.ParseLrc("[00:01.2][00:02.34]Hello\n[00:04:500]World");

        Assert.Equal(3, lines.Count);
        Assert.Equal(new[] { 1200, 2340, 4500 }, lines.Select(line => line.StartMs));
        Assert.Equal("Hello", lines[0].Text);
        Assert.Equal(2340, lines[0].EndMs);
    }

    [Fact]
    public async Task Spotify_session_cookie_is_sent_only_to_fixed_spotify_lyrics_host()
    {
        var handler = new RoutingHandler(_ => JsonResponse("""
        {"lyrics":{"syncType":"LINE_SYNCED","lines":[{"startTimeMs":"1000","endTimeMs":"2000","words":"Hello"}]}}
        """));
        using var source = new SpotifyWebLyricsSource(handler);

        var result = await source.GetLyricsAsync(Track(), "cookie-value");
        var request = Assert.Single(handler.Requests);

        Assert.Equal("spclient.wg.spotify.com", request.RequestUri!.Host);
        Assert.Equal("https", request.RequestUri.Scheme);
        Assert.Equal("sp_dc=cookie-value", Assert.Single(request.Headers.GetValues("Cookie")));
        Assert.Equal("line", result?.SyncLevel);
        Assert.Equal("Hello", Assert.Single(result!.Lines).Text);
    }

    [Fact]
    public async Task Spotify_session_cookie_is_not_forwarded_after_redirect()
    {
        var handler = new RoutingHandler(_ => new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("https://example.invalid/collect") }
        });
        using var source = new SpotifyWebLyricsSource(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => source.GetLyricsAsync(Track(), "cookie-value"));

        Assert.Single(handler.Requests);
        Assert.Equal("spclient.wg.spotify.com", handler.Requests[0].RequestUri!.Host);
    }

    [Fact]
    public async Task Spotify_lyrics_without_cookie_never_makes_a_request()
    {
        var handler = new RoutingHandler(_ => JsonResponse("{}"));
        using var source = new SpotifyWebLyricsSource(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => source.GetLyricsAsync(Track()));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Lyrics_cache_round_trips_data_by_provider_and_track()
    {
        var directory = Path.Combine(Path.GetTempPath(), "OpenOverlay.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new CachedLyricsStore(directory);
            var expected = new LyricsSnapshot("track123", "lrclib", "LRCLIB", "line", [new LyricLineSnapshot("Hello", 1000, 2000)]);
            cache.Write("lrclib", "track123", expected);

            var actual = cache.Read("lrclib", "track123");

            Assert.Equal(expected.TrackId, actual?.TrackId);
            Assert.Equal(expected.SourceId, actual?.SourceId);
            Assert.Equal(expected.Lines, actual?.Lines);
            Assert.Null(cache.Read("spotify", "track123"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static TrackSnapshot Track() => new("track123", "Title", ["Artist"], "Album", null, 180000);

    private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
    };

    private sealed class FakeCredentialStore : OpenOverlay.Core.Abstractions.ICredentialStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
        public string? Read(string name) => _values.GetValueOrDefault(name);
        public void Write(string name, string value) => _values[name] = value;
        public void Delete(string name) => _values.Remove(name);
    }

    private sealed class RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(route(request));
        }
    }
}
