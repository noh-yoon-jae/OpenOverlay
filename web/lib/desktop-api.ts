export type PlaybackStatus = 'disconnected' | 'idle' | 'playing' | 'paused' | 'error'

export type TrackSnapshot = {
  id: string
  title: string
  artists: string[]
  album: string
  albumArtUrl?: string
  durationMs: number
}

export type PlaybackSnapshot = {
  status: PlaybackStatus
  isConnected: boolean
  isPlaying: boolean
  positionMs: number
  observedAtMs: number
  volumePercent?: number | null
  track?: TrackSnapshot
  spotifyLyricsSessionConnected: boolean
  message?: string
}

export type LyricLine = {
  text: string
  startMs: number
  endMs?: number
}

export type LyricsSnapshot = {
  trackId: string
  sourceId: string
  sourceName: string
  syncLevel: 'none' | 'line' | 'word' | 'syllable'
  lines: LyricLine[]
}

export type ProviderId = 'lrclib' | 'spotify'
export type ProviderSettings = Record<ProviderId, boolean>

export type AppSettings = {
  spotifyClientId: string
  alwaysOnTop: boolean
  showProgress: boolean
  ignoreMouseInput: boolean
  overlayOpacity: number
  musicReactiveGlow: boolean
  lyricsFontSize: number
  lyricsFontFamily: 'default' | 'segoe' | 'malgun' | 'arial'
  lyricsHighlightCurrentLine: boolean
  miniLyricsLines: 1 | 3
  lyricsProviderOrder: string[]
  providerEnabled: ProviderSettings
  spotifyLyricsConsentAccepted: boolean
}

export type LyricsProviderHealth = {
  providerId: keyof ProviderSettings
  displayName: string
  attempts: number
  syncedResults: number
  plainResults: number
  notFound: number
  invalidResults: number
  failures: number
  validResults: number
  successRate: number
  syncedRate: number
  lastOutcome: string
  lastCheckedAtMs?: number
}

export type LyricsHealthSnapshot = { providers: LyricsProviderHealth[] }
export type LyricsProviderCheckResult = { providerId: ProviderId; displayName: string; outcome: 'synced' | 'plain' | 'not-found' | 'invalid' | 'error' | 'skipped'; syncLevel: string; lineCount: number; preview: string[]; message?: string }

export type PlaybackAction = 'previous' | 'toggle' | 'next'
export type OverlayMode = 'overlay' | 'lyrics' | 'mini'

export const desktopApi = {
  getSnapshot: () => window.openOverlay.getSnapshot(),
  initialize: () => window.openOverlay.initialize(),
  getSettings: () => window.openOverlay.getSettings(),
  setClientId: (clientId: string) => window.openOverlay.setClientId(clientId),
  connectSpotify: () => window.openOverlay.connectSpotify(),
  disconnectSpotify: () => window.openOverlay.disconnectSpotify(),
  playback: (action: PlaybackAction) => window.openOverlay.playback(action),
  seek: (positionMs: number) => window.openOverlay.seek(positionMs),
  setVolume: (volumePercent: number) => window.openOverlay.setVolume(volumePercent),
  refreshLyrics: () => window.openOverlay.refreshLyrics(),
  setProviderEnabled: (providerId: keyof ProviderSettings, enabled: boolean) =>
    window.openOverlay.setProviderEnabled(providerId, enabled),
  enableSpotifyLyrics: (consented: boolean) => window.openOverlay.enableSpotifyLyrics(consented),
  clearSpotifyLyricsSession: () => window.openOverlay.clearSpotifyLyricsSession(),
  updateSettings: (settings: Partial<AppSettings>) => window.openOverlay.updateSettings(settings),
  getLyricsHealth: () => window.openOverlay.getLyricsHealth(),
  checkLyricsProviders: () => window.openOverlay.checkLyricsProviders(),
  setAlwaysOnTop: (enabled: boolean) => window.openOverlay.setAlwaysOnTop(enabled),
  setOverlayOpacity: (opacity: number) => window.openOverlay.setOverlayOpacity(opacity),
  setOverlayMode: (mode: OverlayMode) => window.openOverlay.setOverlayMode(mode),
  getOverlayMode: () => window.openOverlay.getOverlayMode(),
  completeStartup: () => window.openOverlay.completeStartup(),
  openSettings: () => window.openOverlay.openSettings(),
  closeWindow: () => window.openOverlay.closeWindow(),
  minimizeWindow: () => window.openOverlay.minimizeWindow(),
  onSnapshot: (listener: (snapshot: PlaybackSnapshot) => void) => window.openOverlay.onSnapshot(listener),
  onLyrics: (listener: (lyrics: LyricsSnapshot | null) => void) => window.openOverlay.onLyrics(listener),
  onSettings: (listener: (settings: AppSettings) => void) => window.openOverlay.onSettings(listener),
  onLyricsHealth: (listener: (health: LyricsHealthSnapshot) => void) => window.openOverlay.onLyricsHealth(listener),
  onError: (listener: (message: string) => void) => window.openOverlay.onError(listener),
  onClickThrough: (listener: (enabled: boolean) => void) => window.openOverlay.onClickThrough(listener),
  onOverlayOpacity: (listener: (opacity: number) => void) => window.openOverlay.onOverlayOpacity(listener),
}

declare global {
  interface Window {
    openOverlay: {
      getSnapshot: () => Promise<PlaybackSnapshot>
      initialize: () => Promise<PlaybackSnapshot>
      getSettings: () => Promise<AppSettings>
      getLyricsHealth: () => Promise<LyricsHealthSnapshot>
      checkLyricsProviders: () => Promise<LyricsProviderCheckResult[]>
      setClientId: (clientId: string) => Promise<AppSettings>
      connectSpotify: () => Promise<void>
      disconnectSpotify: () => Promise<void>
      playback: (action: PlaybackAction) => Promise<void>
      seek: (positionMs: number) => Promise<void>
      setVolume: (volumePercent: number) => Promise<void>
      refreshLyrics: () => Promise<void>
      setProviderEnabled: (providerId: ProviderId, enabled: boolean) => Promise<AppSettings>
      enableSpotifyLyrics: (consented: boolean) => Promise<AppSettings>
      clearSpotifyLyricsSession: () => Promise<AppSettings>
      updateSettings: (settings: Partial<AppSettings>) => Promise<AppSettings>
      setAlwaysOnTop: (enabled: boolean) => Promise<boolean>
      setOverlayOpacity: (opacity: number) => Promise<number>
      setOverlayMode: (mode: OverlayMode) => Promise<OverlayMode>
      getOverlayMode: () => Promise<OverlayMode>
      completeStartup: () => Promise<boolean>
      openSettings: () => Promise<void>
      closeWindow: () => Promise<void>
      minimizeWindow: () => Promise<void>
      onSnapshot: (listener: (value: PlaybackSnapshot) => void) => () => void
      onLyrics: (listener: (value: LyricsSnapshot | null) => void) => () => void
      onSettings: (listener: (value: AppSettings) => void) => () => void
      onLyricsHealth: (listener: (value: LyricsHealthSnapshot) => void) => () => void
      onError: (listener: (value: string) => void) => () => void
      onClickThrough: (listener: (value: boolean) => void) => () => void
      onOverlayOpacity: (listener: (value: number) => void) => () => void
    }
  }
}
