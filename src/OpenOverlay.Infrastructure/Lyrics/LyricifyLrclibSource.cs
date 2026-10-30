using System.Reflection;
using System.Text.RegularExpressions;
using OpenOverlay.Core.Abstractions;
using OpenOverlay.Core.Models;

namespace OpenOverlay.Infrastructure.Lyrics;

/// <summary>
/// Thin adapter around Lyricify Lyrics Helper's LRCLIB API. Other package providers are not
/// invoked here; each has its own authentication and network egress behavior.
/// </summary>
public sealed partial class LyricifyLrclibSource : ILyricsSource
{
    public string Id => "lrclib";
    public string DisplayName => "LRCLIB";

    public async Task<LyricsSnapshot?> GetLyricsAsync(TrackSnapshot track, string? spotifyWebSessionCookie = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var api = new Lyricify.Lyrics.Providers.Web.LRCLIB.Api();
        var result = await api.Get(track.Title, string.Join(", ", track.Artists), track.Album, track.DurationMs / 1000d)
            .WaitAsync(cancellationToken);
        if (result is null) return null;

        var resultType = result.GetType();
        var synced = ReadStringProperty(resultType, result, "SyncedLyrics");
        var plain = ReadStringProperty(resultType, result, "PlainLyrics");
        if (!string.IsNullOrWhiteSpace(synced))
        {
            var lines = ParseLrc(synced);
            if (lines.Count > 0) return new LyricsSnapshot(track.Id, Id, DisplayName, "line", lines);
        }
        if (string.IsNullOrWhiteSpace(plain)) return null;
        var plainLines = plain.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(text => new LyricLineSnapshot(text, 0)).ToArray();
        return plainLines.Length == 0 ? null : new LyricsSnapshot(track.Id, Id, DisplayName, "none", plainLines);
    }

    private static string? ReadStringProperty(Type type, object instance, string name) =>
        type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.GetValue(instance) as string;

    internal static IReadOnlyList<LyricLineSnapshot> ParseLrc(string lyrics)
    {
        var parsed = new List<LyricLineSnapshot>();
        foreach (var rawLine in lyrics.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = rawLine.Trim();
            var matches = LrcTimestampRegex().Matches(trimmed);
            if (matches.Count == 0) continue;
            var lastMatch = matches[matches.Count - 1];
            var text = trimmed[(lastMatch.Index + lastMatch.Length)..].Trim();
            if (text.Length == 0) continue;
            foreach (Match match in matches)
            {
                var minute = int.Parse(match.Groups["minutes"].Value, System.Globalization.CultureInfo.InvariantCulture);
                var second = int.Parse(match.Groups["seconds"].Value, System.Globalization.CultureInfo.InvariantCulture);
                var fraction = match.Groups["fraction"].Value;
                var fractionMs = fraction.Length switch
                {
                    1 => int.Parse(fraction, System.Globalization.CultureInfo.InvariantCulture) * 100,
                    2 => int.Parse(fraction, System.Globalization.CultureInfo.InvariantCulture) * 10,
                    3 => int.Parse(fraction, System.Globalization.CultureInfo.InvariantCulture),
                    _ => 0
                };
                parsed.Add(new LyricLineSnapshot(text, checked((minute * 60 + second) * 1000 + fractionMs)));
            }
        }

        var ordered = parsed.OrderBy(line => line.StartMs).ToArray();
        return ordered.Select((line, index) => line with { EndMs = index + 1 < ordered.Length ? ordered[index + 1].StartMs : null }).ToArray();
    }

    [GeneratedRegex(@"\[(?<minutes>\d{1,3}):(?<seconds>\d{2})(?:[.:](?<fraction>\d{1,3}))?\]", RegexOptions.CultureInvariant)]
    private static partial Regex LrcTimestampRegex();
}
