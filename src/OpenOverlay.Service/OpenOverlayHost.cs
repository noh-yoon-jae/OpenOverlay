using System.Text.Json;
using OpenOverlay.Core.Abstractions;
using OpenOverlay.Core.Models;
using OpenOverlay.Core.Policies;
using OpenOverlay.Infrastructure.Lyrics;
using OpenOverlay.Infrastructure.Persistence;
using OpenOverlay.Infrastructure.Security;
using OpenOverlay.Infrastructure.Spotify;

namespace OpenOverlay.Service;

internal sealed class OpenOverlayHost : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ISettingsStore _settingsStore = new JsonSettingsStore();
    private readonly ICredentialStore _credentials = new WindowsCredentialStore();
    private readonly SpotifyPkceClient _spotify;
    private readonly SpotifyWebSessionLogin _spotifySessionLogin = new();
    private readonly ILyricsSource[] _lyricsSources;
    private readonly CachedLyricsStore _lyricsCache = new();
    private readonly LyricsHealthStore _lyricsHealth = new();
    private readonly SemaphoreSlim _lyricsGate = new(1, 1);
    private readonly SemaphoreSlim _outputGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private AppSettings _settings;
    private PlaybackSnapshot _snapshot;
    private LyricsSnapshot? _currentLyrics;

    public OpenOverlayHost()
    {
        _settings = _settingsStore.Load();
        _spotify = new SpotifyPkceClient(_credentials);
        if (!string.IsNullOrWhiteSpace(_settings.SpotifyClientId)) _spotify.SetClientId(_settings.SpotifyClientId);
        _lyricsSources = [new LyricifyLrclibSource(), new SpotifyWebLyricsSource()];
        _credentials.Delete("apple-music-media-user-token");
        _snapshot = new PlaybackSnapshot("disconnected", _spotify.IsAuthenticated, false, 0,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), null, _credentials.Read("spotify-web-sp-dc") is not null);
    }

    public async Task RunAsync()
    {
        Console.InputEncoding = System.Text.Encoding.UTF8;
        Console.OutputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var reader = new StreamReader(Console.OpenStandardInput(), System.Text.Encoding.UTF8);
        using var pollCancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        var pollTask = PollLoopAsync(pollCancellation.Token);

        try
        {
            string? line;
            while ((line = await reader.ReadLineAsync(_shutdown.Token)) is not null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                await HandleLineAsync(line, _shutdown.Token);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        finally
        {
            _shutdown.Cancel();
            pollCancellation.Cancel();
            try { await pollTask; } catch (OperationCanceledException) { }
        }
    }

    private async Task HandleLineAsync(string line, CancellationToken cancellationToken)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(line); }
        catch (JsonException) { await WriteLogAsync("Rejected malformed request JSON."); return; }

        using (document)
        {
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var id) || !root.TryGetProperty("method", out var methodElement)) return;
            var method = methodElement.GetString();
            var parameters = root.TryGetProperty("params", out var paramsElement) ? paramsElement : default;
            try
            {
                var result = await ExecuteAsync(method ?? string.Empty, parameters, cancellationToken);
                await WriteMessageAsync(new { id, result });
            }
            catch (Exception error)
            {
                await WriteLogAsync($"Request {method} failed ({error.GetType().Name}).");
                await WriteMessageAsync(new { id, error = new { message = PublicMessage(error) } });
            }
        }
    }

    private async Task<object?> ExecuteAsync(string method, JsonElement parameters, CancellationToken cancellationToken)
    {
        switch (method)
        {
            case "app.getSnapshot":
                return CurrentSnapshot();
            case "app.initialize":
                return await InitializeAsync(cancellationToken);
            case "settings.get":
                return _settings;
            case "lyrics.health.get":
                return GetLyricsHealthSnapshot();
            case "lyrics.providers.checkAll":
                return await CheckLyricsProvidersAsync(parameters, cancellationToken);
            case "spotify.setClientId":
            {
                var clientId = parameters.ValueKind == JsonValueKind.String ? parameters.GetString()?.Trim() ?? string.Empty : string.Empty;
                _settings.SpotifyClientId = clientId;
                _settingsStore.Save(_settings);
                _spotify.SetClientId(clientId);
                await PublishSettingsAsync();
                return _settings;
            }
            case "spotify.connect":
                await _spotify.ConnectAsync(_settings.SpotifyClientId, cancellationToken);
                await RefreshPlaybackAsync(cancellationToken);
                return null;
            case "spotify.disconnect":
                await _spotify.DisconnectAsync();
                _snapshot = new PlaybackSnapshot("disconnected", false, false, 0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), null,
                    _credentials.Read("spotify-web-sp-dc") is not null);
                _currentLyrics = null;
                await PublishSnapshotAsync();
                await PublishLyricsAsync(null);
                return null;
            case "playback.control":
            {
                var action = parameters.ValueKind == JsonValueKind.String ? parameters.GetString() ?? string.Empty : string.Empty;
                await _spotify.ControlPlaybackAsync(action, cancellationToken);
                await RefreshPlaybackAsync(cancellationToken);
                return null;
            }
            case "playback.setVolume":
            {
                if (parameters.ValueKind != JsonValueKind.Number || !parameters.TryGetInt32(out var volumePercent))
                    throw new InvalidOperationException("Volume must be an integer from 0 to 100.");
                await _spotify.SetVolumeAsync(volumePercent, cancellationToken);
                await RefreshPlaybackAsync(cancellationToken);
                return null;
            }
            case "playback.seek":
            {
                if (parameters.ValueKind != JsonValueKind.Number || !parameters.TryGetInt32(out var positionMs) || positionMs < 0)
                    throw new InvalidOperationException("Playback position must be a non-negative number of milliseconds.");
                var durationMs = _snapshot.Track?.DurationMs ?? 0;
                if (durationMs <= 0) throw new InvalidOperationException("There is no active track to seek.");
                positionMs = Math.Clamp(positionMs, 0, durationMs - 1);
                await _spotify.SeekAsync(positionMs, cancellationToken);
                _snapshot = _snapshot with { PositionMs = positionMs, ObservedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
                await PublishSnapshotAsync();
                return null;
            }
            case "lyrics.refresh":
                await RefreshLyricsAsync(force: true, cancellationToken: cancellationToken);
                return null;
            case "lyrics.setProviderEnabled":
                return await SetProviderEnabledAsync(parameters);
            case "spotifyLyrics.enable":
                return await EnableSpotifyLyricsAsync(parameters, cancellationToken);
            case "spotifyLyrics.clearSession":
                return await ClearSpotifyLyricsSessionAsync();
            case "settings.update":
                return await UpdateSettingsAsync(parameters);
            case "lyrics.adjustOffset":
                return await AdjustLyricsOffsetAsync(parameters);
            default:
                throw new InvalidOperationException($"Unknown backend method: {method}");
        }
    }

    private async Task<AppSettings> SetProviderEnabledAsync(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Array || parameters.GetArrayLength() != 2)
            throw new ArgumentException("Provider settings request was invalid.");
        var id = parameters[0].GetString();
        var enabled = parameters[1].ValueKind == JsonValueKind.True;
        if (id == "spotify" && enabled)
            throw new InvalidOperationException("Enable Spotify lyrics through the consent and sign-in flow in Settings.");
        if (id is null || !_lyricsSources.Any(source => source.Id == id)) throw new ArgumentException("Unknown lyrics provider.");
        _settings.ProviderEnabled[id] = enabled;
        _settingsStore.Save(_settings);
        await PublishSettingsAsync();
        await RefreshLyricsAsync(force: true, cancellationToken: _shutdown.Token);
        return _settings;
    }

    private async Task<AppSettings> EnableSpotifyLyricsAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        LyricsProviderPolicy.RequireSpotifyConsent(parameters.ValueKind == JsonValueKind.True);
        if (!await SpotifyLyricsConsentPrompt.ConfirmAsync())
            throw new InvalidOperationException("Spotify lyrics consent was not accepted. No session cookie was collected.");
        var cookie = await _spotifySessionLogin.CaptureSessionCookieAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(cookie)) throw new InvalidOperationException("Spotify did not provide an sp_dc session cookie.");
        await ClearSpotifyLyricsAccessTokenAsync(cancellationToken);
        _credentials.Write("spotify-web-sp-dc", cookie);
        _settings.SpotifyLyricsConsentAccepted = true;
        _settings.ProviderEnabled["spotify"] = true;
        _settingsStore.Save(_settings);
        await PublishSettingsAsync();
        await PublishSnapshotAsync();
        await RefreshLyricsAsync(force: true, cancellationToken: cancellationToken);
        return _settings;
    }

    private async Task<AppSettings> ClearSpotifyLyricsSessionAsync()
    {
        _credentials.Delete("spotify-web-sp-dc");
        await ClearSpotifyLyricsAccessTokenAsync(_shutdown.Token);
        _settings.ProviderEnabled["spotify"] = false;
        _settings.SpotifyLyricsConsentAccepted = false;
        _settingsStore.Save(_settings);
        _spotifySessionLogin.ClearAppSession();
        await PublishSettingsAsync();
        await PublishSnapshotAsync();
        await RefreshLyricsAsync(force: true, cancellationToken: _shutdown.Token);
        return _settings;
    }

    private Task ClearSpotifyLyricsAccessTokenAsync(CancellationToken cancellationToken) =>
        _lyricsSources.OfType<SpotifyWebLyricsSource>().Single().ClearCachedSessionTokenAsync(cancellationToken);

    private async Task<AppSettings> UpdateSettingsAsync(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object) throw new ArgumentException("Settings update was invalid.");
        var providerOrderChanged = false;
        if (parameters.TryGetProperty("alwaysOnTop", out var alwaysOnTop) && alwaysOnTop.ValueKind is JsonValueKind.True or JsonValueKind.False)
            _settings.AlwaysOnTop = alwaysOnTop.GetBoolean();
        if (parameters.TryGetProperty("showProgress", out var showProgress) && showProgress.ValueKind is JsonValueKind.True or JsonValueKind.False)
            _settings.ShowProgress = showProgress.GetBoolean();
        if (parameters.TryGetProperty("ignoreMouseInput", out var ignoreMouseInput) && ignoreMouseInput.ValueKind is JsonValueKind.True or JsonValueKind.False)
            _settings.IgnoreMouseInput = ignoreMouseInput.GetBoolean();
        if (parameters.TryGetProperty("overlayOpacity", out var overlayOpacity) && overlayOpacity.TryGetDouble(out var opacity))
            _settings.OverlayOpacity = Math.Clamp(opacity, 0.0, 1.0);
        if (parameters.TryGetProperty("musicReactiveGlow", out var musicReactiveGlow) && musicReactiveGlow.ValueKind is JsonValueKind.True or JsonValueKind.False)
            _settings.MusicReactiveGlow = musicReactiveGlow.GetBoolean();
        if (parameters.TryGetProperty("lyricsFontSize", out var lyricsFontSize) && lyricsFontSize.TryGetInt32(out var fontSize))
            _settings.LyricsFontSize = Math.Clamp(fontSize, 18, 48);
        if (parameters.TryGetProperty("lyricsFontFamily", out var lyricsFontFamily) && lyricsFontFamily.ValueKind == JsonValueKind.String)
        {
            var requestedFont = lyricsFontFamily.GetString();
            _settings.LyricsFontFamily = requestedFont is "default" or "segoe" or "malgun" or "arial" ? requestedFont : "default";
        }
        if (parameters.TryGetProperty("lyricsHighlightCurrentLine", out var highlightCurrentLine) && highlightCurrentLine.ValueKind is JsonValueKind.True or JsonValueKind.False)
            _settings.LyricsHighlightCurrentLine = highlightCurrentLine.GetBoolean();
        if (parameters.TryGetProperty("miniLyricsLines", out var miniLyricsLines) && miniLyricsLines.TryGetInt32(out var requestedMiniLyricsLines))
            _settings.MiniLyricsLines = requestedMiniLyricsLines == 3 ? 3 : 1;
        if (parameters.TryGetProperty("lyricsProviderOrder", out var orderElement) && orderElement.ValueKind == JsonValueKind.Array)
        {
            var requestedOrder = JsonSerializer.Deserialize<List<string>>(orderElement.GetRawText(), JsonOptions) ?? [];
            var normalizedOrder = JsonSettingsStore.NormalizeProviderOrder(requestedOrder);
            providerOrderChanged = !_settings.LyricsProviderOrder.SequenceEqual(normalizedOrder, StringComparer.OrdinalIgnoreCase);
            _settings.LyricsProviderOrder = normalizedOrder;
        }
        _settingsStore.Save(_settings);
        await PublishSettingsAsync();
        if (providerOrderChanged) await RefreshLyricsAsync(force: true, cancellationToken: _shutdown.Token);
        return _settings;
    }

    private async Task<AppSettings> AdjustLyricsOffsetAsync(JsonElement parameters)
    {
        if (_snapshot.Track is null) return _settings;
        var delta = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("deltaMs", out var deltaElement) && deltaElement.TryGetInt32(out var value) ? value : 0;
        var trackId = _snapshot.Track.Id;
        _settings.LyricsOffsetsMs.TryGetValue(trackId, out var current);
        _settings.LyricsOffsetsMs[trackId] = Math.Clamp(current + delta, -10_000, 10_000);
        _settingsStore.Save(_settings);
        await PublishLyricsAsync(ApplyOffset(_currentLyrics));
        return _settings;
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            if (!_spotify.IsAuthenticated) continue;
            try { await RefreshPlaybackAsync(cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                _snapshot = _snapshot with { Status = "error", Message = PublicMessage(error), ObservedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
                await PublishSnapshotAsync();
                await WriteLogAsync($"Playback refresh failed ({error.GetType().Name}).");
                await WriteMessageAsync(new { @event = "error", data = PublicMessage(error) });
            }
        }
    }

    private async Task RefreshPlaybackAsync(CancellationToken cancellationToken)
    {
        var previousTrackId = _snapshot.Track?.Id;
        var current = await _spotify.GetPlaybackAsync(cancellationToken);
        _snapshot = current;
        await PublishSnapshotAsync();
        var currentTrackId = current.Track?.Id;
        if (!string.Equals(previousTrackId, currentTrackId, StringComparison.Ordinal))
            await RefreshLyricsAsync(force: false, cancellationToken: cancellationToken);
        else if (current.Track is null && _currentLyrics is not null)
        {
            _currentLyrics = null;
            await PublishLyricsAsync(null);
        }
    }

    private async Task<PlaybackSnapshot> InitializeAsync(CancellationToken cancellationToken)
    {
        if (!_spotify.IsAuthenticated) return CurrentSnapshot();

        try
        {
            _snapshot = await _spotify.GetPlaybackAsync(cancellationToken);
            await PublishSnapshotAsync();
            if (_snapshot.Track is not null) _ = LoadInitialLyricsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            _snapshot = _snapshot with
            {
                Status = "error",
                Message = PublicMessage(error),
                ObservedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            await PublishSnapshotAsync();
            await WriteLogAsync($"Startup Spotify connection check failed ({error.GetType().Name}).");
        }

        return CurrentSnapshot();
    }

    private async Task LoadInitialLyricsAsync(CancellationToken cancellationToken)
    {
        try { await RefreshLyricsAsync(force: false, cancellationToken: cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error) { await WriteLogAsync($"Startup lyrics load failed ({error.GetType().Name}): {PublicMessage(error)}"); }
    }

    private async Task RefreshLyricsAsync(bool force, CancellationToken cancellationToken)
    {
        await _lyricsGate.WaitAsync(cancellationToken);
        try
        {
            var track = _snapshot.Track;
            if (track is null)
            {
                _currentLyrics = null;
                await PublishLyricsAsync(null);
                return;
            }

            var sessionCookie = _credentials.Read("spotify-web-sp-dc");
            string? sourceError = null;
            LyricsSnapshot? plainFallback = null;
            var sourcesById = _lyricsSources.ToDictionary(source => source.Id, StringComparer.OrdinalIgnoreCase);
            var orderedSources = JsonSettingsStore.NormalizeProviderOrder(_settings.LyricsProviderOrder)
                .Where(sourcesById.ContainsKey)
                .Select(id => sourcesById[id])
                .Where(source => LyricsProviderPolicy.CanUse(
                    source.Id,
                    _settings.IsProviderEnabled(source.Id),
                    _settings.SpotifyLyricsConsentAccepted,
                    sessionCookie is not null));

            foreach (var source in orderedSources)
            {
                var cached = force ? null : _lyricsCache.Read(source.Id, track.Id);
                if (cached is not null)
                {
                    var cachedValidation = LyricsHealthChecker.Validate(track, cached);
                    if (!cachedValidation.IsValid)
                    {
                        _lyricsCache.Delete(source.Id, track.Id);
                        await WriteLogAsync($"Discarded invalid cached lyrics from {source.Id}.");
                    }
                    else if (cachedValidation.IsSynchronized)
                    {
                        _currentLyrics = cached;
                        await PublishLyricsAsync(ApplyOffset(cached));
                        return;
                    }
                    else
                    {
                        plainFallback ??= cached;
                        continue;
                    }
                }
                var cookie = source.Id == "spotify" ? sessionCookie : null;
                LyricsSnapshot? lyrics;
                try { lyrics = await source.GetLyricsAsync(track, cookie, cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception error)
                {
                    sourceError = PublicMessage(error);
                    _lyricsHealth.Record(source.Id, "error");
                    await PublishLyricsHealthAsync();
                    await WriteLogAsync($"Lyrics provider {source.Id} failed ({error.GetType().Name}): {PublicMessage(error)}");
                    continue;
                }
                if (lyrics is null)
                {
                    _lyricsHealth.Record(source.Id, "not-found");
                    await PublishLyricsHealthAsync();
                    continue;
                }

                var validation = LyricsHealthChecker.Validate(track, lyrics);
                if (!validation.IsValid)
                {
                    _lyricsHealth.Record(source.Id, "invalid");
                    await PublishLyricsHealthAsync();
                    await WriteLogAsync($"Lyrics provider {source.Id} returned invalid {lyrics.SyncLevel} lyrics; trying the next provider.");
                    continue;
                }

                _lyricsHealth.Record(source.Id, validation.Outcome);
                await PublishLyricsHealthAsync();
                _lyricsCache.Write(source.Id, track.Id, lyrics);
                if (validation.IsSynchronized)
                {
                    _currentLyrics = lyrics;
                    await PublishLyricsAsync(ApplyOffset(lyrics));
                    return;
                }
                plainFallback ??= lyrics;
            }

            if (plainFallback is not null)
            {
                _currentLyrics = plainFallback;
                await PublishLyricsAsync(ApplyOffset(plainFallback));
                return;
            }
            _currentLyrics = null;
            await PublishLyricsAsync(null);
            if (sourceError is not null) await WriteMessageAsync(new { @event = "error", data = sourceError });
        }
        finally { _lyricsGate.Release(); }
    }

    private async Task<IReadOnlyList<object>> CheckLyricsProvidersAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        await _lyricsGate.WaitAsync(cancellationToken);
        try
        {
        var track = _snapshot.Track;
        var suppliedSpotifyCookie = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("spotifySessionCookie", out var cookieElement)
            ? cookieElement.GetString()
            : null;
        if (!string.IsNullOrWhiteSpace(suppliedSpotifyCookie) &&
            (suppliedSpotifyCookie.Length > 4096 || suppliedSpotifyCookie.Any(character => char.IsControl(character) || character == ';')))
            throw new ArgumentException("The supplied Spotify session cookie has an invalid format.");
        if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("track", out var trackElement) && trackElement.ValueKind == JsonValueKind.Object)
        {
            var id = trackElement.TryGetProperty("id", out var idElement) ? idElement.GetString()?.Trim() : null;
            var title = trackElement.TryGetProperty("title", out var titleElement) ? titleElement.GetString()?.Trim() : null;
            var artists = trackElement.TryGetProperty("artists", out var artistsElement) && artistsElement.ValueKind == JsonValueKind.Array
                ? artistsElement.EnumerateArray().Select(item => item.GetString()?.Trim()).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().ToArray()
                : [];
            var album = trackElement.TryGetProperty("album", out var albumElement) ? albumElement.GetString()?.Trim() ?? string.Empty : string.Empty;
            var durationMs = trackElement.TryGetProperty("durationMs", out var durationElement) && durationElement.TryGetInt32(out var duration) ? duration : 0;
            if (string.IsNullOrWhiteSpace(title) || artists.Length == 0) throw new ArgumentException("Track title and at least one artist are required.");
            track = new TrackSnapshot(string.IsNullOrWhiteSpace(id) ? $"diagnostic-{Guid.NewGuid():N}" : id, title, artists, album, null, Math.Max(0, durationMs));
        }
        if (track is null) throw new InvalidOperationException("Connect Spotify or provide track title and artist details.");

        var results = new List<object>();
        // A script-provided cookie is ephemeral: never read or write the stored Spotify session in this diagnostic path.
        var cookie = suppliedSpotifyCookie ?? _credentials.Read("spotify-web-sp-dc");
        foreach (var id in JsonSettingsStore.NormalizeProviderOrder(_settings.LyricsProviderOrder))
        {
            var source = _lyricsSources.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (source is null) continue;
            var enabled = _settings.IsProviderEnabled(id);
            var usable = id == "spotify" && !string.IsNullOrWhiteSpace(suppliedSpotifyCookie)
                ? true
                : LyricsProviderPolicy.CanUse(id, enabled, _settings.SpotifyLyricsConsentAccepted, cookie is not null);
            if (id == "spotify" && track.Id.StartsWith("diagnostic-", StringComparison.Ordinal))
                usable = false;
            if (!usable)
            {
                var reason = id == "spotify" && track.Id.StartsWith("diagnostic-", StringComparison.Ordinal)
                    ? "A Spotify track ID is required for this provider."
                    : enabled ? "Provider needs its credential or consent." : "Provider is disabled.";
                results.Add(new { providerId = id, displayName = source.DisplayName, outcome = "skipped", syncLevel = "none", lineCount = 0, preview = Array.Empty<string>(), message = reason });
                continue;
            }

            try
            {
                var lyrics = await source.GetLyricsAsync(track, id == "spotify" ? cookie : null, cancellationToken);
                if (lyrics is null)
                {
                    _lyricsHealth.Record(id, "not-found");
                    results.Add(new { providerId = id, displayName = source.DisplayName, outcome = "not-found", syncLevel = "none", lineCount = 0, preview = Array.Empty<string>(), message = (string?)null });
                    continue;
                }
                var validation = LyricsHealthChecker.Validate(track, lyrics);
                _lyricsHealth.Record(id, validation.Outcome);
                if (validation.IsValid) _lyricsCache.Write(id, track.Id, lyrics);
                results.Add(new { providerId = id, displayName = source.DisplayName, outcome = validation.IsValid ? validation.Outcome : "invalid", syncLevel = lyrics.SyncLevel, lineCount = lyrics.Lines.Count, preview = lyrics.Lines.Take(3).Select(line => line.Text).ToArray(), message = validation.IsValid ? null : "The response failed lyric validation." });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                _lyricsHealth.Record(id, "error");
                results.Add(new { providerId = id, displayName = source.DisplayName, outcome = "error", syncLevel = "none", lineCount = 0, preview = Array.Empty<string>(), message = PublicMessage(error) });
            }
            await PublishLyricsHealthAsync();
        }
        return results;
        }
        finally { _lyricsGate.Release(); }
    }

    private LyricsSnapshot? ApplyOffset(LyricsSnapshot? lyrics)
    {
        if (lyrics is null || !_settings.LyricsOffsetsMs.TryGetValue(lyrics.TrackId, out var offset) || offset == 0) return lyrics;
        var shifted = lyrics.Lines.Select(line => line with
        {
            StartMs = Math.Max(0, line.StartMs + offset),
            EndMs = line.EndMs.HasValue ? Math.Max(0, line.EndMs.Value + offset) : null
        }).OrderBy(line => line.StartMs).ToArray();
        return lyrics with { Lines = shifted };
    }

    private PlaybackSnapshot CurrentSnapshot() => _snapshot with
    {
        IsConnected = _spotify.IsAuthenticated,
        SpotifyLyricsSessionConnected = _credentials.Read("spotify-web-sp-dc") is not null
    };

    private async Task PublishSnapshotAsync() => await WriteMessageAsync(new { @event = "snapshot", data = CurrentSnapshot() });
    private async Task PublishLyricsAsync(LyricsSnapshot? value) => await WriteMessageAsync(new { @event = "lyrics", data = value });
    private async Task PublishSettingsAsync() => await WriteMessageAsync(new { @event = "settings", data = _settings });
    private LyricsHealthSnapshot GetLyricsHealthSnapshot() => _lyricsHealth.Snapshot(_lyricsSources.Select(source => (source.Id, source.DisplayName)));
    private async Task PublishLyricsHealthAsync() => await WriteMessageAsync(new { @event = "lyrics.health", data = GetLyricsHealthSnapshot() });

    private async Task WriteMessageAsync<T>(T message)
    {
        var line = JsonSerializer.Serialize(message, JsonOptions);
        await _outputGate.WaitAsync();
        try { await Console.Out.WriteLineAsync(line); await Console.Out.FlushAsync(); }
        finally { _outputGate.Release(); }
    }

    private static Task WriteLogAsync(string message)
    {
        Console.Error.WriteLine($"[OpenOverlay backend] {message}");
        return Task.CompletedTask;
    }

    private static string PublicMessage(Exception error) => error switch
    {
        HttpRequestException => "A network request failed. Check your connection and try again.",
        OperationCanceledException => "The request was canceled.",
        _ => error.Message
    };

    public ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _spotify.Dispose();
        foreach (var source in _lyricsSources.OfType<IDisposable>()) source.Dispose();
        _lyricsGate.Dispose();
        _outputGate.Dispose();
        _shutdown.Dispose();
        return ValueTask.CompletedTask;
    }
}
