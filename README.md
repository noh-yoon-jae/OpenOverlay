# OpenOverlay

[English](README.md) · [한국어](README.ko.md)

OpenOverlay is a Windows Spotify lyrics overlay. The desktop shell is Electron, the interface is built with Next.js and Tailwind CSS, and a .NET 10 service handles Spotify and lyric providers.

## Features

### Player modes and startup

- **Full player:** album artwork and track details, lyrics, playback progress, transport controls, and Spotify volume control. Drag the progress slider to seek within a track.
- **Lyrics only:** synchronized lyrics, a seekable progress slider, and previous, play/pause, and next controls.
- **Mini player:** compact artwork and track/lyric information with playback controls. Expand it to return to the full player.
- Use the mode buttons in the title bar to switch modes. Each mode has a fixed window size selected for its layout.
- At launch, a compact startup view waits for settings and the initial Spotify playback query. The window then changes to the saved player mode and size.

### Lyrics display

- Search enabled lyric providers for the current Spotify track and identify the provider used in the player header.
- Synchronized lyrics highlight the current line and follow playback automatically. The lyrics viewport shows five lines at a time while the complete lyric text remains scrollable.
- Scroll the lyrics with the mouse wheel to inspect other lines. Automatic following resumes three seconds after scrolling stops.
- Plain lyrics without timestamps cannot follow playback or highlight a time-aligned current line.
- Use **Compare sources** in settings to query enabled providers for the current track and review their results and quality statistics.

### Spotify playback

- Connect with a Spotify Developer app using Authorization Code with PKCE.
- Control the active Spotify session with previous track, play/pause, and next track. The full player also provides a volume slider.
- Click or drag the playback slider to seek within the current track. Spotify's Seek API requires a Premium account and active playback device.
- Playback status is polled by the local service; the interface interpolates the playback position between updates.

### Lyrics providers and quality

- **LRCLIB** is enabled by default and can return synchronized or plain lyrics.
- **Spotify lyrics (experimental)** is disabled by default. Enable it in settings and complete the separate consent and Spotify web sign-in flow before use.
- Turn providers on or off individually and reorder their search priority. Open the provider comparison and statistics in settings to review results and synchronized-lyrics quality.
- Spotify web-session lyrics use a private, undocumented endpoint and may stop working when Spotify changes it. This is separate from Spotify OAuth playback access.

### Settings and overlay controls

- Adjust lyric font, font size, and current-line highlighting; show or hide the playback progress display.
- Choose whether the overlay stays above other windows, whether it ignores mouse input, and the background opacity.
- Enable or disable the music-reactive edge glow.
- Move the overlay by dragging its title bar. Its position is saved locally between runs.
- Use global shortcuts: **Ctrl+Alt+O** shows or hides the overlay, **Ctrl+Alt+P** toggles click-through, and **Ctrl+Alt+Left/Right** shifts lyric timing by 250 ms.
- Open settings in a separate window from the player.

## Architecture and privacy

- **Layered .NET solution:** `OpenOverlay.Core` contains shared models, abstractions, and policies; `OpenOverlay.Infrastructure` implements Spotify, lyric providers, persistence, and credential security; `OpenOverlay.Service` hosts the command dispatcher; `OpenOverlay.Tests` contains backend tests.
- **Next.js and Tailwind CSS** provide the renderer UI and are statically exported for packaging. UI routes and components live under `web/app` and `web/components`; shared client-side types and desktop bridge helpers live under `web/lib`.
- **Electron** owns the overlay and separate settings windows, window modes, global shortcuts, and app protocol. `desktop/preload.cjs` exposes a limited allowlisted API; renderer Node.js access is disabled.
- **Process communication:** the renderer calls the preload API, which sends allowlisted IPC requests to Electron. Electron forwards backend commands as JSON lines over the .NET process's standard input and receives responses/events on standard output. The backend does not run a persistent local HTTP API server.
- **Spotify sign-in exception:** during PKCE authorization only, .NET temporarily listens on the loopback address `127.0.0.1:43821` to receive Spotify's OAuth redirect, then closes the listener. This is not an app API endpoint.
- Spotify OAuth tokens are stored in the Windows Credential Manager. The Spotify lyrics session is optional and stored separately there after consent. Its `sp_dc` cookie is read only from the app's dedicated WebView2 profile, not the system browser profile.
- The `sp_dc` cookie is used only for the experimental Spotify lyrics flow: it is sent to `open.spotify.com` to obtain a short-lived player token, and that token is sent to `spclient.wg.spotify.com`. The cookie is not exposed to the renderer. Disabling the provider stops its use; **Clear session** removes the saved session and the app's WebView2 profile data.
- Lyrics cache: `%LOCALAPPDATA%\OpenOverlay\LyricsCache`.
- Non-secret settings: `%LOCALAPPDATA%\OpenOverlay\settings.json`.

## Requirements

- Windows 10 or later
- Node.js 22 or later
- pnpm 11.25 or later (Corepack can provide pnpm)
- .NET 10 SDK
- Microsoft Edge WebView2 Runtime for the optional Spotify lyrics sign-in window

## Development

Install dependencies and run the desktop app with the Next.js development server:

```powershell
corepack pnpm install
corepack pnpm dev
```

Useful project commands:

```powershell
corepack pnpm typecheck
corepack pnpm build:web
corepack pnpm build:backend
corepack pnpm build
```

`corepack pnpm dev` starts the web development server, Electron, and the .NET service. `corepack pnpm build` builds the static web UI and Windows backend, then packages the Windows installer.


## Spotify Developer setup

Create a Spotify Developer app and register this redirect URI:

```text
http://127.0.0.1:43821/callback/
```

Enter the app's Client ID in OpenOverlay settings. The app uses Authorization Code with PKCE and requests the playback scopes needed for reading playback state and controlling playback. Spotify Development Mode currently requires an eligible Premium developer account and limits an app to five authorized users; consult Spotify's current [quota modes](https://developer.spotify.com/documentation/web-api/concepts/quota-modes) and [Developer Terms](https://developer.spotify.com/terms).

## Licenses

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and `src/OpenOverlay.Infrastructure/LICENSES` for third-party notices and licenses.
