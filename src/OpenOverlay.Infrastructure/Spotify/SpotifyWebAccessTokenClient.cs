using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenOverlay.Infrastructure.Spotify;

/// <summary>
/// Exchanges the app-only Spotify web session cookie for the short-lived Web Player
/// bearer token used by Spotify's color-lyrics endpoint. Flow follows Lyricify Lyrics Helper.
/// </summary>
public sealed class SpotifyWebAccessTokenClient : IDisposable
{
    private const string TokenUrl = "https://open.spotify.com/api/token";
    private const string ServerTimeUrl = "https://open.spotify.com/api/server-time";
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";
    private const string BundledSecretJson = "{\"59\":[123,105,79,70,110,59,52,125,60,49,80,70,89,75,80,86,63,53,123,37,117,49,52,93,77,62,47,86,48,104,68,72],\"60\":[79,109,69,123,90,65,46,74,94,34,58,48,70,71,92,85,122,63,91,64,87,87],\"61\":[44,55,47,42,70,40,34,114,76,74,50,111,120,97,75,76,94,102,43,69,49,120,118,80,64,78]}";
    private static readonly Uri[] SecretSources =
    [
        new("https://code.thetadev.de/ThetaDev/spotify-secrets/raw/branch/main/secrets/secretDict.json"),
        new("https://raw.githubusercontent.com/Thereallo1026/spotify-secrets/refs/heads/main/secrets/secretDict.json"),
        new("https://raw.githubusercontent.com/xyloflake/spot-secrets-go/main/secrets/secretDict.json")
    ];

    private readonly HttpClient _spotifyHttp = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(12) };
    private readonly HttpClient _secretHttp = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(8) };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private long _expiresAtMs;

    public async Task<string> GetAccessTokenAsync(string spDc, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && !string.IsNullOrWhiteSpace(_token) && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < _expiresAtMs - 60_000)
                return _token;

            var token = await RefreshAsync(spDc, cancellationToken);
            _token = token.AccessToken;
            _expiresAtMs = token.ExpiresAtMs;
            return _token;
        }
        finally { _gate.Release(); }
    }

    public async Task ClearCachedTokenAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _token = null;
            _expiresAtMs = 0;
        }
        finally { _gate.Release(); }
    }

    private async Task<TokenPayload> RefreshAsync(string spDc, CancellationToken cancellationToken)
    {
        var (secret, version) = await GetTotpSecretAsync(cancellationToken);
        var serverTime = await GetServerTimeAsync(cancellationToken);
        var totp = GenerateTotp(serverTime, secret);

        var response = await SendTokenRequestAsync(spDc, totp, version, legacy: false, cancellationToken);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            response.Dispose();
            response = await SendTokenRequestAsync(spDc, totp, version, legacy: true, cancellationToken);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new InvalidOperationException("Spotify rejected the stored sp_dc session during Web Player token exchange. Clear the session and sign in again.");
            if ((int)response.StatusCode is >= 300 and < 400)
                throw new InvalidOperationException("Spotify redirected the Web Player token request; OpenOverlay rejected the redirect.");
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var accessToken = root.TryGetProperty("accessToken", out var access) ? access.GetString() : null;
            var expiry = root.TryGetProperty("accessTokenExpirationTimestampMs", out var expires) && expires.TryGetInt64(out var timestamp) ? timestamp : 0;
            var anonymous = root.TryGetProperty("isAnonymous", out var anon) && anon.ValueKind == JsonValueKind.True;
            if (anonymous || string.IsNullOrWhiteSpace(accessToken) || expiry <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                throw new InvalidOperationException("Spotify returned an anonymous or expired Web Player token for this session.");
            return new TokenPayload(accessToken, expiry);
        }
    }

    private async Task<HttpResponseMessage> SendTokenRequestAsync(string spDc, string totp, string version, bool legacy, CancellationToken cancellationToken)
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("reason", legacy ? "transport" : "init"),
            new("productType", "web-player"),
            new("totp", totp),
            new("totpVer", version),
            new("totpServer", totp)
        };
        if (legacy) parameters.Add(new("ts", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()));
        var query = string.Join("&", parameters.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        using var request = new HttpRequestMessage(HttpMethod.Get, TokenUrl + "?" + query);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("App-platform", "WebPlayer");
        request.Headers.TryAddWithoutValidation("Origin", "https://open.spotify.com");
        request.Headers.Referrer = new Uri("https://open.spotify.com/");
        request.Headers.TryAddWithoutValidation("Cookie", "sp_dc=" + spDc);
        return await _spotifyHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private async Task<long> GetServerTimeAsync(CancellationToken cancellationToken)
    {
        using var response = await _spotifyHttp.GetAsync(ServerTimeUrl, cancellationToken);
        if ((int)response.StatusCode is >= 300 and < 400) throw new InvalidOperationException("Spotify redirected the server-time request; OpenOverlay rejected the redirect.");
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return document.RootElement.TryGetProperty("serverTime", out var time) && time.TryGetInt64(out var seconds)
            ? seconds
            : throw new InvalidOperationException("Spotify returned an invalid server time.");
    }

    private async Task<(string Secret, string Version)> GetTotpSecretAsync(CancellationToken cancellationToken)
    {
        foreach (var source in SecretSources)
        {
            try
            {
                using var response = await _secretHttp.GetAsync(source, cancellationToken);
                if ((int)response.StatusCode is >= 300 and < 400) continue;
                response.EnsureSuccessStatusCode();
                var raw = await response.Content.ReadAsStringAsync(cancellationToken);
                if (TryParseSecret(raw, out var current)) return current;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
        }
        if (TryParseSecret(BundledSecretJson, out var fallback)) return fallback;
        throw new InvalidOperationException("Could not load Spotify's Web Player token signing secret.");
    }

    private static bool TryParseSecret(string json, out (string Secret, string Version) result)
    {
        result = default;
        try
        {
            using var document = JsonDocument.Parse(json);
            var latest = document.RootElement.EnumerateObject()
                .Where(property => int.TryParse(property.Name, out _))
                .OrderByDescending(property => int.Parse(property.Name))
                .FirstOrDefault();
            if (latest.Value.ValueKind != JsonValueKind.Array) return false;
            var values = latest.Value.EnumerateArray().Select(value => value.GetInt32()).ToArray();
            var secret = string.Concat(values.Select((value, index) => (value ^ ((index % 33) + 9)).ToString()));
            if (string.IsNullOrWhiteSpace(secret)) return false;
            result = (secret, latest.Name);
            return true;
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (FormatException) { return false; }
    }

    private static string GenerateTotp(long serverTimeSeconds, string secret)
    {
        var counter = serverTimeSeconds / 30;
        var counterBytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(counter));
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(counterBytes);
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    public void Dispose()
    {
        _gate.Dispose();
        _spotifyHttp.Dispose();
        _secretHttp.Dispose();
    }

    private sealed record TokenPayload(string AccessToken, long ExpiresAtMs);
}
