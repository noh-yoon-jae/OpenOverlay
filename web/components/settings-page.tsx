'use client'

import { useCallback, useEffect, useState } from 'react'
import { ChevronDown, ChevronUp, X } from 'lucide-react'
import { AppSettings, LyricsHealthSnapshot, LyricsProviderCheckResult, PlaybackSnapshot, ProviderId, ProviderSettings, desktopApi } from '@/lib/desktop-api'

const providers: { id: ProviderId; name: string; description: string }[] = [
  { id: 'lrclib', name: 'LRCLIB', description: 'Synchronized and plain lyrics; no key needed' },
  { id: 'spotify', name: 'Spotify (experimental)', description: 'Uses the optional Spotify web session' },
]

const defaultSettings: AppSettings = {
  spotifyClientId: '',
  alwaysOnTop: true,
  showProgress: true,
  ignoreMouseInput: false,
  overlayOpacity: 0.75,
  musicReactiveGlow: true,
  lyricsFontSize: 24,
  lyricsFontFamily: 'default',
  lyricsHighlightCurrentLine: true,
  miniLyricsLines: 1,
  lyricsProviderOrder: ['lrclib', 'spotify'],
  providerEnabled: { lrclib: true, spotify: false },
  spotifyLyricsConsentAccepted: false,
}

const emptySnapshot: PlaybackSnapshot = {
  status: 'disconnected',
  isConnected: false,
  isPlaying: false,
  positionMs: 0,
  observedAtMs: Date.now(),
  spotifyLyricsSessionConnected: false,
}

function SettingRow({ label, description, checked, onChange, disabled = false }: {
  label: string
  description: string
  checked: boolean
  onChange: (value: boolean) => void
  disabled?: boolean
}) {
  return <label className={`flex items-center justify-between gap-4 p-4 ${disabled ? 'cursor-not-allowed opacity-50' : 'cursor-pointer'}`}>
    <span><span className="block text-sm">{label}</span><span className="mt-1 block text-xs text-muted-foreground">{description}</span></span>
    <button type="button" role="switch" aria-checked={checked} aria-label={label} disabled={disabled} onClick={() => onChange(!checked)} className={`relative h-6 w-11 shrink-0 rounded-full transition-colors disabled:cursor-not-allowed ${checked ? 'bg-spotify' : 'bg-white/15'}`}>
      <span className={`absolute top-1 size-4 rounded-full bg-white transition-transform ${checked ? 'left-6' : 'left-1'}`} />
    </button>
  </label>
}

export default function SettingsPage() {
  const [snapshot, setSnapshot] = useState<PlaybackSnapshot>(emptySnapshot)
  const [settings, setSettings] = useState<AppSettings>(defaultSettings)
  const [lyricsHealth, setLyricsHealth] = useState<LyricsHealthSnapshot>({ providers: [] })
  const [providerChecks, setProviderChecks] = useState<LyricsProviderCheckResult[] | null>(null)
  const [clientIdDraft, setClientIdDraft] = useState('')
  const [showConsent, setShowConsent] = useState(false)
  const [consentChecked, setConsentChecked] = useState(false)
  const [busy, setBusy] = useState(false)
  const [settingsSaveState, setSettingsSaveState] = useState<'loading' | 'saved' | 'saving' | 'error'>('loading')
  const [error, setError] = useState('')

  const applySettings = useCallback((value: AppSettings) => setSettings({
    ...defaultSettings,
    ...value,
    ignoreMouseInput: Boolean(value.ignoreMouseInput),
    overlayOpacity: Number.isFinite(value.overlayOpacity) ? Math.min(1, Math.max(0, value.overlayOpacity)) : 0.75,
    lyricsFontSize: Number.isFinite(value.lyricsFontSize) ? Math.min(48, Math.max(18, value.lyricsFontSize)) : 24,
    lyricsFontFamily: ['default', 'segoe', 'malgun', 'arial'].includes(value.lyricsFontFamily) ? value.lyricsFontFamily : 'default',
    lyricsHighlightCurrentLine: value.lyricsHighlightCurrentLine !== false,
    miniLyricsLines: value.miniLyricsLines === 3 ? 3 : 1,
  }), [])

  const run = useCallback(async (operation: () => Promise<unknown>) => {
    setBusy(true)
    setError('')
    try { await operation() }
    catch (cause) { setError(cause instanceof Error ? cause.message : 'Something went wrong.') }
    finally { setBusy(false) }
  }, [])

  useEffect(() => {
    let mounted = true
    const unsubscribers = [
      desktopApi.onSnapshot((value) => { if (mounted) setSnapshot(value) }),
      desktopApi.onSettings((value) => { if (mounted) { applySettings(value); setClientIdDraft(value.spotifyClientId); setSettingsSaveState('saved') } }),
      desktopApi.onLyricsHealth((value) => { if (mounted) setLyricsHealth(value) }),
      desktopApi.onError((value) => { if (mounted) setError(value) }),
    ]
    desktopApi.getSnapshot().then((value) => { if (mounted) setSnapshot(value) }).catch((cause) => { if (mounted) setError(cause.message) })
    desktopApi.getSettings().then((value) => { if (mounted) { applySettings(value); setClientIdDraft(value.spotifyClientId); setSettingsSaveState('saved') } }).catch((cause) => { if (mounted) { setError(cause.message); setSettingsSaveState('error') } })
    desktopApi.getLyricsHealth().then((value) => { if (mounted) setLyricsHealth(value) }).catch((cause) => { if (mounted) setError(cause.message) })
    return () => { mounted = false; unsubscribers.forEach((unsubscribe) => unsubscribe()) }
  }, [])

  const saveSettingsAction = async (operation: () => Promise<unknown>) => {
    setBusy(true)
    setError('')
    setSettingsSaveState('saving')
    try {
      await operation()
      setSettingsSaveState('saved')
    } catch (cause) {
      setSettingsSaveState('error')
      setError(cause instanceof Error ? cause.message : 'Could not save settings.')
    } finally {
      setBusy(false)
    }
  }

  const saveClientId = () => saveSettingsAction(async () => {
    applySettings(await desktopApi.setClientId(clientIdDraft.trim()))
  })

  const toggleTop = (enabled: boolean) => {
    setSettings((previous) => ({ ...previous, alwaysOnTop: enabled }))
    void saveSettingsAction(async () => {
      await desktopApi.setAlwaysOnTop(enabled)
      applySettings(await desktopApi.updateSettings({ alwaysOnTop: enabled }))
    })
  }

  const toggleMouseIgnore = (enabled: boolean) => {
    setSettings((previous) => ({ ...previous, ignoreMouseInput: enabled }))
    void saveSettingsAction(async () => applySettings(await desktopApi.updateSettings({ ignoreMouseInput: enabled })))
  }

  const updateOpacityPreview = (opacity: number) => {
    setSettings((previous) => ({ ...previous, overlayOpacity: opacity }))
    void desktopApi.setOverlayOpacity(opacity).catch((cause) => setError(cause.message))
  }

  const saveOpacity = () => {
    const opacity = Number.isFinite(settings.overlayOpacity) ? Math.min(1, Math.max(0, settings.overlayOpacity)) : 0.75
    void saveSettingsAction(async () => applySettings(await desktopApi.updateSettings({ overlayOpacity: opacity })))
  }

  const setProvider = (providerId: keyof ProviderSettings, enabled: boolean) => {
    void saveSettingsAction(async () => applySettings(await desktopApi.setProviderEnabled(providerId, enabled)))
  }

  const moveProvider = (providerId: keyof ProviderSettings, direction: -1 | 1) => {
    const order = [...settings.lyricsProviderOrder]
    const index = order.indexOf(providerId)
    const targetIndex = index + direction
    if (index < 0 || targetIndex < 0 || targetIndex >= order.length) return
    ;[order[index], order[targetIndex]] = [order[targetIndex], order[index]]
    void saveSettingsAction(async () => applySettings(await desktopApi.updateSettings({ lyricsProviderOrder: order })))
  }

  const saveTypography = async (patch: Partial<AppSettings>) => {
    await saveSettingsAction(async () => applySettings(await desktopApi.updateSettings(patch)))
  }

  const healthById = new Map(lyricsHealth.providers.map((provider) => [provider.providerId, provider]))
  const currentOpacity = Number.isFinite(settings.overlayOpacity)
    ? Math.min(1, Math.max(0, settings.overlayOpacity))
    : 0.75

  const acceptSpotifyLyrics = () => {
    if (!consentChecked) return
    setShowConsent(false)
    void saveSettingsAction(async () => applySettings(await desktopApi.enableSpotifyLyrics(true)))
  }

  return <main className="settings-page-shell h-screen min-h-0 overflow-hidden p-2 text-foreground sm:p-3">
    <section className="mx-auto flex h-[calc(100vh-16px)] min-h-0 w-full max-w-[620px] flex-col overflow-hidden rounded-2xl border border-white/[0.06] bg-panel shadow-xl sm:h-[calc(100vh-24px)]">
      <header className="drag-region flex shrink-0 items-center justify-between border-b border-white/[0.06] px-4 py-3">
        <div><p className="text-sm font-medium">Settings</p><p className="mt-1 text-xs text-muted-foreground">Customize your overlay and lyrics sources</p></div>
        <span aria-live="polite" className={`text-[10px] ${settingsSaveState === 'error' ? 'text-red-300' : settingsSaveState === 'saving' || settingsSaveState === 'loading' ? 'text-amber-200' : 'text-muted-foreground/70'}`}>{settingsSaveState === 'loading' ? 'Loading…' : settingsSaveState === 'saving' ? 'Saving…' : settingsSaveState === 'error' ? 'Save failed' : 'All changes saved'}</span>
        <button onClick={() => void desktopApi.closeWindow()} aria-label="Close settings" className="no-drag icon-button"><X className="size-4" /></button>
      </header>
      <div className="settings-content min-h-0 flex-1">
        <section><p className="text-xs font-medium uppercase tracking-[0.16em] text-muted-foreground">Spotify</p><div className="mt-3 rounded-xl border border-white/[0.08] p-4">
          <label className="block text-xs text-muted-foreground" htmlFor="spotify-client-id">Developer app Client ID</label><div className="mt-2 flex gap-2"><input id="spotify-client-id" value={clientIdDraft} onChange={(event) => setClientIdDraft(event.target.value)} placeholder="Paste your Spotify Client ID" className="min-w-0 flex-1 rounded-lg border border-white/10 bg-black/20 px-3 py-2 text-sm outline-none focus:border-spotify/60" /><button disabled={busy || clientIdDraft.trim() === settings.spotifyClientId} onClick={() => void saveClientId()} className="rounded-lg bg-white/10 px-3 text-xs hover:bg-white/15 disabled:opacity-40">Save</button></div>
          <p className="mt-2 text-[11px] leading-relaxed text-muted-foreground">The Client ID is stored locally. Register the redirect URI shown in the setup guide in Spotify Developer Dashboard.</p>
          {snapshot.isConnected && <button onClick={() => void run(() => desktopApi.disconnectSpotify())} className="mt-3 text-xs text-muted-foreground underline underline-offset-4 hover:text-foreground">Disconnect Spotify</button>}
        </div></section>
        <section><p className="text-xs font-medium uppercase tracking-[0.16em] text-muted-foreground">Lyrics sources</p><div className="mt-3 divide-y divide-white/[0.08] rounded-xl border border-white/[0.08]">
          {providers.map((provider) => <SettingRow key={provider.id} label={provider.name} description={provider.description} checked={settings.providerEnabled[provider.id]} onChange={(enabled) => {
            if (provider.id === 'spotify' && enabled) { setConsentChecked(false); setShowConsent(true) }
            else setProvider(provider.id, enabled)
          }} disabled={busy} />)}
        </div>
          <p className="mt-2 text-[11px] leading-relaxed text-muted-foreground">Spotify web-session lyrics use a private web endpoint and may stop working if Spotify changes its service. This is separate from Spotify OAuth.</p>
          <div className="mt-3 flex items-center justify-between rounded-xl border border-white/[0.08] px-4 py-3"><div><p className="text-xs font-medium">Spotify lyrics session</p><p className="mt-1 text-[11px] text-muted-foreground">{snapshot.spotifyLyricsSessionConnected ? 'Session stored securely on this Windows account' : 'No session stored'}</p></div>{snapshot.spotifyLyricsSessionConnected && <button disabled={busy} onClick={() => void saveSettingsAction(async () => applySettings(await desktopApi.clearSpotifyLyricsSession()))} className="text-xs text-muted-foreground underline underline-offset-4 hover:text-foreground">Clear session</button>}</div>
        </section>
        <section><div className="flex items-center justify-between gap-3"><div><p className="text-xs font-medium uppercase tracking-[0.16em] text-muted-foreground">Provider priority & health</p><p className="mt-1 text-[11px] text-muted-foreground">Compare enabled providers for the current song. Checks update the local quality statistics.</p></div><button disabled={busy || !snapshot.track} onClick={() => void run(async () => setProviderChecks(await desktopApi.checkLyricsProviders()))} className="shrink-0 rounded-lg border border-white/[0.08] px-3 py-2 text-[11px] text-muted-foreground hover:bg-white/[0.04] disabled:opacity-40">Compare providers</button></div>
          {providerChecks && <div className="mt-3 space-y-2 rounded-xl border border-white/[0.06] p-3"><div className="flex items-center justify-between"><p className="text-xs font-medium">Latest comparison</p><button onClick={() => setProviderChecks(null)} className="text-[10px] text-muted-foreground hover:text-foreground">Dismiss</button></div>{providerChecks.map((result) => <div key={result.providerId} className="border-t border-white/[0.06] pt-2 first:border-0 first:pt-0"><div className="flex items-center justify-between gap-2"><p className="text-xs">{result.displayName}</p><span className={`text-[10px] ${result.outcome === 'synced' ? 'text-spotify' : result.outcome === 'error' || result.outcome === 'invalid' ? 'text-red-300' : 'text-muted-foreground'}`}>{result.outcome}{result.syncLevel !== 'none' ? ` · ${result.syncLevel}` : ''}{result.lineCount > 0 ? ` · ${result.lineCount} lines` : ''}</span></div>{result.preview.length > 0 && <p className="mt-1 line-clamp-2 text-[10px] leading-relaxed text-muted-foreground">{result.preview.join(' / ')}</p>}{result.message && <p className="mt-1 text-[10px] text-muted-foreground/70">{result.message}</p>}</div>)}</div>}
          <ol className="mt-3 space-y-2">{settings.lyricsProviderOrder.map((providerId, index) => {
            const health = healthById.get(providerId as keyof ProviderSettings)
            const name = providers.find((provider) => provider.id === providerId)?.name ?? providerId
            const enabled = settings.providerEnabled[providerId as keyof ProviderSettings]
            return <li key={providerId} className="flex items-center gap-3 rounded-xl border border-white/[0.06] px-3 py-2.5">
              <span className="flex size-6 shrink-0 items-center justify-center rounded-full bg-white/[0.05] text-[11px] text-muted-foreground">{index + 1}</span>
              <div className="min-w-0 flex-1"><div className="flex items-center gap-2"><p className="truncate text-xs font-medium">{name}</p><span className={`text-[10px] ${enabled ? 'text-spotify' : 'text-muted-foreground/60'}`}>{enabled ? 'On' : 'Off'}</span></div><p className="mt-1 truncate text-[10px] text-muted-foreground">{!health || health.attempts === 0 ? 'No checks yet' : `${health.syncedResults} synced · ${health.plainResults} plain · ${health.notFound} missing · ${health.invalidResults + health.failures} failed`}</p>{health && health.attempts > 0 && <p className="mt-0.5 text-[10px] text-muted-foreground/70">{Math.round(health.successRate * 100)}% valid · {Math.round(health.syncedRate * 100)}% synced</p>}</div>
              <div className="flex shrink-0"><button disabled={busy || index === 0} onClick={() => moveProvider(providerId as keyof ProviderSettings, -1)} aria-label={`Move ${name} up`} className="icon-button size-7 disabled:opacity-30"><ChevronUp className="size-4" /></button><button disabled={busy || index === settings.lyricsProviderOrder.length - 1} onClick={() => moveProvider(providerId as keyof ProviderSettings, 1)} aria-label={`Move ${name} down`} className="icon-button size-7 disabled:opacity-30"><ChevronDown className="size-4" /></button></div>
            </li>
          })}</ol>
          <p className="mt-2 text-[10px] text-muted-foreground/70">{lyricsHealth.providers.some((provider) => provider.attempts > 0) ? `Valid response rate: ${Math.round(lyricsHealth.providers.reduce((sum, provider) => sum + provider.validResults, 0) / Math.max(1, lyricsHealth.providers.reduce((sum, provider) => sum + provider.attempts, 0)) * 100)}% · Stored on this PC` : 'Source quality statistics build as songs are checked.'}</p>
        </section>
        <section>
          <p className="text-xs font-medium uppercase tracking-[0.16em] text-muted-foreground">Lyrics display</p>
          <div className="mt-3 divide-y divide-white/[0.08] rounded-xl border border-white/[0.08]">
            <div className="flex items-center justify-between gap-4 p-4"><div><p className="text-sm">Lyrics font</p><p className="mt-1 text-xs text-muted-foreground">Choose a font installed on Windows.</p></div><select aria-label="Lyrics font" disabled={busy} value={settings.lyricsFontFamily} onChange={(event) => { const lyricsFontFamily = event.target.value as AppSettings['lyricsFontFamily']; setSettings((previous) => ({ ...previous, lyricsFontFamily })); void saveTypography({ lyricsFontFamily }) }} className="rounded-lg border border-white/10 bg-[#111614] px-3 py-2 text-xs text-foreground outline-none focus:border-spotify/60"><option value="default">Default</option><option value="segoe">Segoe UI</option><option value="malgun">맑은 고딕</option><option value="arial">Arial</option></select></div>
            <div className="p-4"><div className="flex items-center justify-between gap-4"><div><p className="text-sm">Current line size</p><p className="mt-1 text-xs text-muted-foreground">Adjust the highlighted lyric size.</p></div><span className="text-xs tabular-nums text-muted-foreground">{settings.lyricsFontSize}px</span></div><input aria-label="Lyrics font size" type="range" min="18" max="48" step="1" value={settings.lyricsFontSize} disabled={busy} onChange={(event) => setSettings((previous) => ({ ...previous, lyricsFontSize: Number(event.target.value) }))} onPointerUp={(event) => void saveTypography({ lyricsFontSize: Number(event.currentTarget.value) })} onKeyUp={(event) => void saveTypography({ lyricsFontSize: Number(event.currentTarget.value) })} className="mt-4 w-full accent-[#1ed760] disabled:opacity-50" /><div className="mt-1 flex justify-between text-[10px] text-muted-foreground/70"><span>18px</span><span>48px</span></div></div>
            <SettingRow label="Highlight current line" description="Make the synchronized line brighter and bolder" checked={settings.lyricsHighlightCurrentLine} onChange={(lyricsHighlightCurrentLine) => { setSettings((previous) => ({ ...previous, lyricsHighlightCurrentLine })); void saveTypography({ lyricsHighlightCurrentLine }) }} disabled={busy} />
            <div className="bg-black/15 p-4">
              <p className="mb-2 text-[10px] uppercase tracking-wider text-muted-foreground">Preview · 5 lines</p>
              <div style={{ fontFamily: settings.lyricsFontFamily === 'segoe' ? 'Segoe UI, sans-serif' : settings.lyricsFontFamily === 'malgun' ? 'Malgun Gothic, sans-serif' : settings.lyricsFontFamily === 'arial' ? 'Arial, sans-serif' : 'var(--font-geist-sans), sans-serif' }} className="space-y-1.5 text-center">
                {Array.from({ length: 5 }, (_, index) => {
                  const current = index === 2
                  return <p key={index} style={current ? { fontSize: `${settings.lyricsFontSize}px` } : undefined} className={`truncate leading-tight ${current && settings.lyricsHighlightCurrentLine ? 'font-semibold text-foreground' : 'text-muted-foreground/50'}`}>{current ? 'Current lyric line preview' : 'Nearby lyric line preview'}</p>
                })}
              </div>
            </div>
          </div>
        </section>
        <section>
          <p className="text-xs font-medium uppercase tracking-[0.16em] text-muted-foreground">Mini player</p>
          <div className="mt-3 rounded-xl border border-white/[0.08] p-4">
            <div className="flex items-start justify-between gap-4">
              <div><p className="text-sm">Lyrics lines</p><p className="mt-1 text-xs text-muted-foreground">Choose how many lyric lines appear in the mini player.</p></div>
              <div role="group" aria-label="Mini player lyric lines" className="flex shrink-0 rounded-lg border border-white/10 bg-white/[0.025] p-1">
                {[1, 3].map((lineCount) => <button key={lineCount} type="button" aria-pressed={settings.miniLyricsLines === lineCount} disabled={busy} onClick={() => { const miniLyricsLines = lineCount as 1 | 3; setSettings((previous) => ({ ...previous, miniLyricsLines })); void saveSettingsAction(async () => applySettings(await desktopApi.updateSettings({ miniLyricsLines }))) }} className={`rounded-md px-3 py-1.5 text-xs transition-colors disabled:opacity-50 ${settings.miniLyricsLines === lineCount ? 'bg-white/[0.12] text-foreground' : 'text-muted-foreground hover:text-foreground'}`}>{lineCount} line{lineCount === 1 ? '' : 's'}</button>)}
              </div>
            </div>
          </div>
        </section>
        <section><p className="text-xs font-medium uppercase tracking-[0.16em] text-muted-foreground">Appearance & interaction</p><div className="mt-3 divide-y divide-white/[0.08] rounded-xl border border-white/[0.08]"><SettingRow label="Always on top" description="Keep OpenOverlay above other apps" checked={settings.alwaysOnTop} onChange={toggleTop} disabled={busy} /><SettingRow label="Ignore mouse input" description="Clicks pass through the overlay. Press Ctrl+Alt+P to toggle it back." checked={settings.ignoreMouseInput} onChange={toggleMouseIgnore} disabled={busy} /><SettingRow label="Show progress" description="Display current playback time and progress" checked={settings.showProgress} onChange={(enabled) => { setSettings((previous) => ({ ...previous, showProgress: enabled })); void saveSettingsAction(async () => applySettings(await desktopApi.updateSettings({ showProgress: enabled }))) }} disabled={busy} /><SettingRow label="Music-reactive glow" description="Soft color glow breathes while music is playing." checked={settings.musicReactiveGlow} onChange={(enabled) => { setSettings((previous) => ({ ...previous, musicReactiveGlow: enabled })); void saveSettingsAction(async () => applySettings(await desktopApi.updateSettings({ musicReactiveGlow: enabled }))) }} disabled={busy} /><div className="p-4"><div className="flex items-center justify-between gap-4"><div><p className="text-sm">Overlay background opacity</p><p className="mt-1 text-xs text-muted-foreground">From text-only transparency to a solid background. Text stays clear.</p></div><span className="text-xs tabular-nums text-muted-foreground">{Math.round(currentOpacity * 100)}%</span></div><input aria-label="Overlay background opacity" type="range" min="0" max="100" step="1" value={Math.round(currentOpacity * 100)} disabled={busy} onChange={(event) => updateOpacityPreview(Number(event.target.value) / 100)} onPointerUp={saveOpacity} onKeyUp={saveOpacity} className="mt-4 w-full accent-[#1ed760] disabled:opacity-50" /><div className="mt-1 flex justify-between text-[10px] text-muted-foreground/70"><span>Text only · 0%</span><span>Solid · 100%</span></div></div></div></section>
        {error && <p role="alert" className="rounded-lg border border-red-400/20 bg-red-400/10 px-3 py-2 text-xs text-red-200">{error}</p>}
      </div>
      <footer className="shrink-0 border-t border-white/[0.06] p-3">
        <button onClick={() => void desktopApi.closeWindow()} className="w-full rounded-xl bg-foreground py-3 text-sm font-medium text-background hover:opacity-90">Done</button>
      </footer>
    </section>

    {showConsent && <div className="settings-backdrop consent-backdrop"><section role="dialog" aria-modal="true" aria-labelledby="consent-title" className="consent-panel">
      <div className="flex items-start justify-between"><div><p className="text-sm font-semibold" id="consent-title">Enable Spotify lyrics source?</p><p className="mt-2 text-xs leading-relaxed text-muted-foreground">OpenOverlay will open an app-only Spotify login window. After you sign in and confirm, it stores only the <code>sp_dc</code> session cookie in Windows Credential Manager. The cookie is sent to <code>open.spotify.com</code> to obtain a short-lived Web Player token; that token is sent to Spotify's lyrics endpoint. OpenOverlay also downloads a public token-signing secret from public mirrors, without sending your cookie. Your cookie is never sent to the UI or your system browser profile.</p></div><button onClick={() => setShowConsent(false)} aria-label="Close consent" className="icon-button -mr-2 -mt-2"><X className="size-4" /></button></div>
      <p className="mt-3 rounded-lg border border-amber-300/15 bg-amber-300/[0.07] p-3 text-[11px] leading-relaxed text-amber-100/80">This experimental provider uses Spotify's web session. Its compatibility and permission under Spotify's terms are not guaranteed.</p>
      <label className="mt-4 flex cursor-pointer items-start gap-2 text-xs leading-relaxed text-foreground/80"><input type="checkbox" checked={consentChecked} onChange={(event) => setConsentChecked(event.target.checked)} className="mt-0.5 accent-[#1ed760]" /><span>I understand and consent to storing this Spotify session cookie for the lyrics source.</span></label>
      <div className="mt-5 flex justify-end gap-2"><button onClick={() => setShowConsent(false)} className="rounded-lg px-3 py-2 text-xs text-muted-foreground hover:bg-white/[0.06]">Cancel</button><button disabled={!consentChecked || busy} onClick={acceptSpotifyLyrics} className="rounded-lg bg-spotify px-3 py-2 text-xs font-semibold text-black disabled:opacity-40">Continue to Spotify login</button></div>
    </section></div>}
  </main>
}
