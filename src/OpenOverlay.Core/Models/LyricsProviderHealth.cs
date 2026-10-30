namespace OpenOverlay.Core.Models;

public sealed record LyricsProviderHealth(
    string ProviderId,
    string DisplayName,
    int Attempts,
    int SyncedResults,
    int PlainResults,
    int NotFound,
    int InvalidResults,
    int Failures,
    string LastOutcome,
    long? LastCheckedAtMs)
{
    public int ValidResults => SyncedResults + PlainResults;
    public double SuccessRate => Attempts == 0 ? 0 : (double)ValidResults / Attempts;
    public double SyncedRate => ValidResults == 0 ? 0 : (double)SyncedResults / ValidResults;
}

public sealed record LyricsHealthSnapshot(IReadOnlyList<LyricsProviderHealth> Providers);
