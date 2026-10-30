using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Windows.Forms;

namespace OpenOverlay.Infrastructure.Spotify;

public sealed class SpotifyWebSessionLogin
{
    private readonly string _userDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenOverlay",
        "SpotifyWebSession");

    public async Task<string> CaptureSessionCookieAsync(CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        var thread = new Thread(() =>
        {
            try
            {
                string? cookie;
                using (var form = new SpotifyLoginForm(_userDataFolder, completion))
                {
                    form.ShowDialog();
                    cookie = form.CapturedCookie;
                }
                if (!completion.Task.IsCompleted)
                {
                    if (cookie is not null) completion.TrySetResult(cookie);
                    else completion.TrySetCanceled();
                }
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
            }
        }) { IsBackground = true, Name = "OpenOverlay Spotify sign-in" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return await completion.Task;
    }

    public void ClearAppSession()
    {
        var root = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenOverlay"));
        var target = Path.GetFullPath(_userDataFolder);
        if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to clear a WebView2 profile outside the OpenOverlay data folder.");
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
    }

    private sealed class SpotifyLoginForm : Form
    {
        private readonly string _userDataFolder;
        private readonly TaskCompletionSource<string> _completion;
        private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
        private readonly Label _status = new() { AutoSize = true, Text = "Sign in to Spotify, then choose Use this session." };
        private readonly Button _useSession = new() { AutoSize = true, Text = "Use this session" };
        public string? CapturedCookie { get; private set; }

        public SpotifyLoginForm(string userDataFolder, TaskCompletionSource<string> completion)
        {
            _userDataFolder = userDataFolder;
            _completion = completion;
            Text = "Connect Spotify lyrics session";
            Width = 900;
            Height = 720;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;

            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(10, 8, 10, 8), WrapContents = false };
            toolbar.Controls.Add(_status);
            toolbar.Controls.Add(_useSession);
            Controls.Add(_webView);
            Controls.Add(toolbar);
            _useSession.Click += async (_, _) => await CompleteLoginAsync();
            Shown += async (_, _) => await InitializeBrowserAsync();
            FormClosed += (_, _) => _webView.Dispose();
        }

        private async Task InitializeBrowserAsync()
        {
            try
            {
                Directory.CreateDirectory(_userDataFolder);
                var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder);
                await _webView.EnsureCoreWebView2Async(environment);
                _webView.CoreWebView2.NavigationStarting += (_, args) =>
                {
                    if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var target) || target.Scheme != Uri.UriSchemeHttps || !IsSpotifyHost(target.Host))
                    {
                        args.Cancel = true;
                        _status.Text = "A non-Spotify navigation was blocked.";
                    }
                };
                _webView.CoreWebView2.Navigate("https://accounts.spotify.com/login?continue=https%3A%2F%2Fopen.spotify.com%2F");
            }
            catch (Exception error)
            {
                _status.Text = "Could not start Spotify's app-only sign-in window.";
                _completion.TrySetException(new InvalidOperationException(_status.Text, error));
                Close();
            }
        }

        private async Task CompleteLoginAsync()
        {
            if (_webView.CoreWebView2 is null) return;
            _useSession.Enabled = false;
            try
            {
                var cookies = await _webView.CoreWebView2.CookieManager.GetCookiesAsync("https://open.spotify.com/");
                var cookie = cookies.FirstOrDefault(item => item.Name == "sp_dc" && IsSpotifyHost(item.Domain.TrimStart('.')));
                if (cookie is null || string.IsNullOrWhiteSpace(cookie.Value))
                {
                    _status.Text = "Spotify's sp_dc session cookie was not found. Finish signing in and try again.";
                    _useSession.Enabled = true;
                    return;
                }
                CapturedCookie = cookie.Value;
                Close();
            }
            catch
            {
                _status.Text = "Could not read the Spotify session. Try signing in again.";
                _useSession.Enabled = true;
            }
        }

        private static bool IsSpotifyHost(string host) =>
            host.Equals("spotify.com", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".spotify.com", StringComparison.OrdinalIgnoreCase);
    }
}
