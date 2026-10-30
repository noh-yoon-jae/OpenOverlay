using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenOverlay.Core.Abstractions;
using OpenOverlay.Core.Models;

namespace OpenOverlay.Infrastructure.Spotify;

public sealed class SpotifyPkceClient(ICredentialStore credentials, HttpMessageHandler? handler = null) : ISpotifyClient, IDisposable
{
    public const string RedirectUri = "http://127.0.0.1:43821/callback/";
    private static readonly string[] Scopes = ["user-read-playback-state", "user-modify-playback-state"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = CreateHttpClient(handler);
    private string? _clientId;
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(credentials.Read("spotify-refresh-token"));

    public void SetClientId(string clientId) => _clientId = clientId.Trim();

    public async Task ConnectAsync(string clientId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId)) throw new InvalidOperationException("Save your Spotify Client ID in Settings first.");
        _clientId = clientId.Trim();
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));

        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 43821);
        try { listener.Start(); }
        catch (SocketException error)
        {
            throw new InvalidOperationException("Spotify sign-in could not open its local callback. Close another OpenOverlay sign-in and try again.", error);
        }

        var query = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["response_type"] = "code",
            ["redirect_uri"] = RedirectUri,
            ["scope"] = string.Join(' ', Scopes),
            ["code_challenge_method"] = "S256",
            ["code_challenge"] = challenge,
            ["state"] = state
        };
        var authorizeUri = "https://accounts.spotify.com/authorize?" + string.Join("&", query.Select(pair =>
            Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
        Process.Start(new ProcessStartInfo(authorizeUri) { UseShellExecute = true });

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(4));
        TcpClient client;
        try { client = await listener.AcceptTcpClientAsync(timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Spotify sign-in timed out. Try connecting again.");
        }

        using (client)
        {
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync(timeout.Token);
            if (string.IsNullOrWhiteSpace(requestLine)) throw new InvalidOperationException("Spotify returned an empty callback.");
            var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            var callback = parts.Length >= 2 ? new Uri(new Uri(RedirectUri), parts[1]) : null;
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(timeout.Token))) { }

            try
            {
                if (parts[0] != "GET" || callback is null || callback.Host != "127.0.0.1" || callback.Port != 43821 ||
                    !string.Equals(callback.AbsolutePath, "/callback/", StringComparison.Ordinal))
                    throw new InvalidOperationException("Spotify returned to an unexpected callback path.");
                var queryValues = ParseQuery(callback.Query);
                var returnedState = queryValues.GetValueOrDefault("state") ?? string.Empty;
                if (!StateMatches(state, returnedState))
                    throw new InvalidOperationException("Spotify sign-in state did not match. The callback was rejected.");
                if (queryValues.GetValueOrDefault("error") is { Length: > 0 } authError)
                    throw new InvalidOperationException($"Spotify sign-in was not completed ({authError}).");
                var code = queryValues.GetValueOrDefault("code");
                if (string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("Spotify did not return an authorization code.");

                await ExchangeCodeAsync(code, verifier, cancellationToken);
                await WriteCallbackPageAsync(stream, "Spotify is connected. You can return to OpenOverlay.", cancellationToken);
            }
            catch
            {
                await WriteCallbackPageAsync(stream, "OpenOverlay could not complete Spotify sign-in. Return to the app and try again.", cancellationToken);
                throw;
            }
        }
    }

    public Task DisconnectAsync()
    {
        credentials.Delete("spotify-refresh-token");
        _accessToken = null;
        _accessTokenExpiresAt = default;
        return Task.CompletedTask;
    }

    public async Task<PlaybackSnapshot> GetPlaybackAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendApiAsync(HttpMethod.Get, "https://api.spotify.com/v1/me/player", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent)
            return Snapshot("idle", true, 0, null, null);

        await EnsureSuccessAsync(response, cancellationToken);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return MapPlayback(document.RootElement, IsAuthenticated, credentials.Read("spotify-web-sp-dc") is not null);
    }

    public async Task ControlPlaybackAsync(string action, CancellationToken cancellationToken = default)
    {
        var (method, path) = action switch
        {
            "previous" => (HttpMethod.Post, "previous"),
            "toggle" => (HttpMethod.Put, "playpause"),
            "next" => (HttpMethod.Post, "next"),
            _ => throw new ArgumentException("Unknown Spotify playback action.", nameof(action))
        };

        if (action == "toggle")
        {
            var snapshot = await GetPlaybackAsync(cancellationToken);
            path = snapshot.IsPlaying ? "pause" : "play";
        }

        using var response = await SendApiAsync(method, $"https://api.spotify.com/v1/me/player/{path}", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
    {
        if (volumePercent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(volumePercent));
        using var response = await SendApiAsync(HttpMethod.Put,
            $"https://api.spotify.com/v1/me/player/volume?volume_percent={volumePercent}", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task SeekAsync(int positionMs, CancellationToken cancellationToken = default)
    {
        if (positionMs < 0) throw new ArgumentOutOfRangeException(nameof(positionMs));
        using var response = await SendApiAsync(HttpMethod.Put,
            $"https://api.spotify.com/v1/me/player/seek?position_ms={positionMs}", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new InvalidOperationException("Seeking requires Spotify Premium and an active playback device. Check your Spotify app permissions too.");
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public void Dispose() => _http.Dispose();

    internal static bool StateMatches(string expected, string actual) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));

    internal static PlaybackSnapshot MapPlayback(JsonElement root, bool isConnected, bool hasWebSession)
    {
        var isPlaying = root.TryGetProperty("is_playing", out var playingElement) && playingElement.GetBoolean();
        var positionMs = root.TryGetProperty("progress_ms", out var progressElement) && progressElement.TryGetInt32(out var progress) ? progress : 0;
        int? volumePercent = root.TryGetProperty("device", out var device) && device.ValueKind == JsonValueKind.Object &&
            device.TryGetProperty("volume_percent", out var volumeElement) && volumeElement.TryGetInt32(out var volume) ? volume : null;
        TrackSnapshot? track = null;
        if (root.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object &&
            (!item.TryGetProperty("type", out var type) || type.GetString() == "track"))
        {
            var id = item.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            var title = item.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(title))
            {
                var artists = item.TryGetProperty("artists", out var artistItems) && artistItems.ValueKind == JsonValueKind.Array
                    ? artistItems.EnumerateArray().Select(artist => artist.TryGetProperty("name", out var artistName) ? artistName.GetString() : null).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().ToArray()
                    : [];
                var album = item.TryGetProperty("album", out var albumElement) ? albumElement : default;
                var albumName = album.ValueKind == JsonValueKind.Object && album.TryGetProperty("name", out var albumNameElement) ? albumNameElement.GetString() ?? string.Empty : string.Empty;
                var imageUrl = album.ValueKind == JsonValueKind.Object && album.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array && images.GetArrayLength() > 0 && images[0].TryGetProperty("url", out var image)
                    ? image.GetString()
                    : null;
                var durationMs = item.TryGetProperty("duration_ms", out var durationElement) && durationElement.TryGetInt32(out var duration) ? duration : 0;
                track = new TrackSnapshot(id, title, artists, albumName, imageUrl, durationMs);
            }
        }

        var status = track is null ? "idle" : isPlaying ? "playing" : "paused";
        return new PlaybackSnapshot(status, isConnected, isPlaying, positionMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), track, hasWebSession,
            VolumePercent: volumePercent);
    }

    private PlaybackSnapshot Snapshot(string status, bool isPlaying, int positionMs, TrackSnapshot? track, string? message) =>
        new(status, IsAuthenticated, isPlaying, positionMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), track,
            credentials.Read("spotify-web-sp-dc") is not null, message);

    private async Task<HttpResponseMessage> SendApiAsync(HttpMethod method, string uri, HttpContent? content, CancellationToken cancellationToken, bool retryUnauthorized = true)
    {
        var accessToken = await GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized && retryUnauthorized)
        {
            response.Dispose();
            _accessToken = null;
            return await SendApiAsync(method, uri, content, cancellationToken, retryUnauthorized: false);
        }
        return response;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_accessToken) && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30)) return _accessToken;
        var refreshToken = credentials.Read("spotify-refresh-token");
        if (string.IsNullOrWhiteSpace(refreshToken)) throw new InvalidOperationException("Spotify is not connected. Connect Spotify in the overlay settings.");
        _clientId ??= Environment.GetEnvironmentVariable("OPENOVERLAY_SPOTIFY_CLIENT_ID");
        if (string.IsNullOrWhiteSpace(_clientId)) throw new InvalidOperationException("Spotify Client ID is missing. Save it in Settings and reconnect.");

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        });
        using var response = await _http.PostAsync("https://accounts.spotify.com/api/token", form, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Spotify returned an empty token response.");
        _accessToken = token.AccessToken;
        _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
        if (!string.IsNullOrWhiteSpace(token.RefreshToken)) credentials.Write("spotify-refresh-token", token.RefreshToken);
        return _accessToken;
    }

    private async Task ExchangeCodeAsync(string code, string verifier, CancellationToken cancellationToken)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _clientId!,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["code_verifier"] = verifier
        });
        using var response = await _http.PostAsync("https://accounts.spotify.com/api/token", form, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Spotify returned an empty token response.");
        if (string.IsNullOrWhiteSpace(token.RefreshToken)) throw new InvalidOperationException("Spotify did not return a refresh token.");
        credentials.Write("spotify-refresh-token", token.RefreshToken);
        _accessToken = token.AccessToken;
        _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.RequestMessage?.RequestUri is { Host: "accounts.spotify.com", AbsolutePath: "/api/token" } &&
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("Spotify authorization expired or was rejected. Reconnect Spotify in Settings.");
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("Spotify authorization expired. Reconnect Spotify in Settings.");
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new InvalidOperationException("Spotify rejected this action. Check your account, active playback device, and app permissions.");
        if (response.StatusCode == (HttpStatusCode)429)
            throw new InvalidOperationException("Spotify is rate limiting requests. OpenOverlay will retry during the next playback refresh.");
        throw new HttpRequestException($"Spotify request failed ({(int)response.StatusCode}). {body}", null, response.StatusCode);
    }

    private static async Task WriteCallbackPageAsync(Stream stream, string message, CancellationToken cancellationToken)
    {
        var html = "<!doctype html><meta charset=utf-8><title>OpenOverlay</title><p>" + WebUtility.HtmlEncode(message) + "</p>";
        var bytes = Encoding.UTF8.GetBytes(html);
        var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nConnection: close\r\nContent-Length: {bytes.Length}\r\n\r\n");
        await stream.WriteAsync(headers, cancellationToken);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static Dictionary<string, string> ParseQuery(string query) => query.TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2))
        .Where(parts => parts.Length > 0)
        .ToDictionary(parts => Uri.UnescapeDataString(parts[0].Replace('+', ' ')),
            parts => parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : string.Empty,
            StringComparer.Ordinal);

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static HttpClient CreateHttpClient(HttpMessageHandler? handler) =>
        new(handler ?? new HttpClientHandler(), disposeHandler: true) { Timeout = TimeSpan.FromSeconds(15) };

    private sealed class TokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }
}
