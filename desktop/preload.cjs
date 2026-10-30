const { contextBridge, ipcRenderer } = require('electron')

const invoke = async (channel, ...args) => {
  const result = await ipcRenderer.invoke(channel, ...args)
  if (!result.ok) throw new Error(result.error)
  return result.value
}
const subscribe = (channel, listener) => {
  const wrapped = (_event, value) => listener(value)
  ipcRenderer.on(channel, wrapped)
  return () => ipcRenderer.removeListener(channel, wrapped)
}

contextBridge.exposeInMainWorld('openOverlay', {
  getSnapshot: () => invoke('app:get-snapshot'),
  initialize: () => invoke('app:initialize'),
  getSettings: () => invoke('settings:get'),
  getLyricsHealth: () => invoke('lyrics:health-get'),
  checkLyricsProviders: () => invoke('lyrics:providers-check'),
  setClientId: (clientId) => invoke('spotify:set-client-id', clientId),
  connectSpotify: () => invoke('spotify:connect'),
  disconnectSpotify: () => invoke('spotify:disconnect'),
  playback: (action) => invoke('playback:control', action),
  seek: (positionMs) => invoke('playback:seek', positionMs),
  setVolume: (volumePercent) => invoke('playback:set-volume', volumePercent),
  refreshLyrics: () => invoke('lyrics:refresh'),
  setProviderEnabled: (providerId, enabled) => invoke('lyrics:set-provider-enabled', providerId, enabled),
  enableSpotifyLyrics: (consented) => invoke('spotify-lyrics:enable', consented),
  clearSpotifyLyricsSession: () => invoke('spotify-lyrics:clear-session'),
  updateSettings: (settings) => invoke('settings:update', settings),
  setAlwaysOnTop: (enabled) => invoke('window:set-always-on-top', enabled),
  setOverlayOpacity: (opacity) => invoke('window:set-overlay-opacity', opacity),
  setOverlayMode: (mode) => invoke('window:set-overlay-mode', mode),
  getOverlayMode: () => invoke('window:get-overlay-mode'),
  completeStartup: () => invoke('window:startup-complete'),
  openSettings: () => invoke('window:open-settings'),
  closeWindow: () => invoke('window:close'),
  minimizeWindow: () => invoke('window:minimize'),
  onSnapshot: (listener) => subscribe('app:snapshot', listener),
  onLyrics: (listener) => subscribe('app:lyrics', listener),
  onSettings: (listener) => subscribe('app:settings', listener),
  onLyricsHealth: (listener) => subscribe('app:lyrics-health', listener),
  onError: (listener) => subscribe('app:error', listener),
  onClickThrough: (listener) => subscribe('app:click-through', listener),
  onOverlayOpacity: (listener) => subscribe('app:overlay-opacity', listener),
})
