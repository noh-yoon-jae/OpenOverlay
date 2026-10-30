namespace OpenOverlay.Core.Models;

public sealed class AppSettings
{
    public string SpotifyClientId { get; set; } = string.Empty;
    public bool AlwaysOnTop { get; set; } = true;
    public bool ShowProgress { get; set; } = true;
    public bool IgnoreMouseInput { get; set; }
    public double OverlayOpacity { get; set; } = 0.75;
    public bool MusicReactiveGlow { get; set; } = true;
    public int LyricsFontSize { get; set; } = 24;
    public string LyricsFontFamily { get; set; } = "default";
    public bool LyricsHighlightCurrentLine { get; set; } = true;
    public int MiniLyricsLines { get; set; } = 1;
    public bool SpotifyLyricsConsentAccepted { get; set; }
    public List<string> LyricsProviderOrder { get; set; } = ["lrclib", "spotify"];
    public Dictionary<string, bool> ProviderEnabled { get; set; } = CreateDefaultProviderSettings();
    public Dictionary<string, int> LyricsOffsetsMs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsProviderEnabled(string id) => ProviderEnabled.TryGetValue(id, out var enabled) && enabled;

    public static Dictionary<string, bool> CreateDefaultProviderSettings() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["lrclib"] = true, ["spotify"] = false
    };
}
