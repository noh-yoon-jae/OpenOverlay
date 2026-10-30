namespace OpenOverlay.Core.Policies;

public static class LyricsProviderPolicy
{
    public static bool CanUse(string providerId, bool enabled, bool consentAccepted, bool hasSpotifySession) =>
        enabled && (providerId != "spotify" || consentAccepted && hasSpotifySession);

    public static void RequireSpotifyConsent(bool consented)
    {
        if (!consented) throw new InvalidOperationException("Spotify web-session consent is required before sign-in.");
    }
}
