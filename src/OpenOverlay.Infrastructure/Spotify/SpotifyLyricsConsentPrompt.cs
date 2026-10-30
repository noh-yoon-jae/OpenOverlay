using System.Windows.Forms;

namespace OpenOverlay.Infrastructure.Spotify;

public static class SpotifyLyricsConsentPrompt
{
    public static Task<bool> ConfirmAsync()
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var result = MessageBox.Show(
                    "OpenOverlay will open a private Spotify sign-in window and store only the sp_dc session cookie in this Windows account's Credential Manager. The cookie is sent to open.spotify.com to obtain a short-lived Web Player token; that token is sent to Spotify's lyrics endpoint at spclient.wg.spotify.com. OpenOverlay also downloads a public rotating token-signing secret from listed public mirrors (no account cookie is sent to those mirrors). The provider is experimental.\n\nDo you consent and want to continue?",
                    "Enable Spotify lyrics (experimental)",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                completion.TrySetResult(result == DialogResult.Yes);
            }
            catch (Exception error) { completion.TrySetException(error); }
        }) { IsBackground = true, Name = "OpenOverlay Spotify lyrics consent" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
