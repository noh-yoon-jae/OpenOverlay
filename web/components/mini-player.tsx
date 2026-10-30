import type { CSSProperties } from 'react'
import { LayoutGrid, Maximize2, Settings2, X } from 'lucide-react'
import type { LyricsSnapshot, OverlayMode, PlaybackAction, TrackSnapshot } from '@/lib/desktop-api'
import { Controls, ScrollingText, SpotifyMark } from '@/components/player-primitives'
import { miniLyricSlots } from '@/lib/lyrics-view-model.cjs'

type MiniPlayerProps = {
  track?: TrackSnapshot
  lyrics: LyricsSnapshot | null
  currentLineIndex: number
  miniLyricsLines: 1 | 3
  isPlaying: boolean
  lyricsLoading: boolean
  clickThrough: boolean
  panelStyle: CSSProperties
  panelGlowClass: string
  onModeChange: (mode: OverlayMode) => void
  onControl: (action: PlaybackAction) => void
  onSettings: () => void
  onClose: () => void
}

export function MiniPlayer({
  track,
  lyrics,
  currentLineIndex,
  miniLyricsLines,
  isPlaying,
  lyricsLoading,
  clickThrough,
  panelStyle,
  panelGlowClass,
  onModeChange,
  onControl,
  onSettings,
  onClose,
}: MiniPlayerProps) {
  return <main className="mini-overlay-root relative isolate flex min-h-screen items-center p-2 text-foreground">
    <section style={panelStyle} className={`drag-region relative z-10 mx-auto flex w-full max-w-[620px] rounded-2xl border ${miniLyricsLines === 3 ? 'p-2' : 'p-3'} transition-shadow duration-1000 ${miniLyricsLines === 3 ? 'h-[calc(100vh-16px)] items-stretch gap-3' : 'items-center gap-3'} ${panelGlowClass}`}>
      {!track ? <>
        <div className="flex size-14 shrink-0 items-center justify-center rounded-lg bg-white/[0.05] text-spotify"><SpotifyMark /></div>
        <div className="min-w-0 flex-1"><p className="truncate text-sm font-medium">Nothing is playing</p><p className="mt-1 truncate text-xs text-muted-foreground">Play a song in Spotify to show lyrics.</p></div>
        <div className="no-drag flex shrink-0 items-center gap-1">
          <button onClick={() => onModeChange('overlay')} aria-label="Full player" title="Full player" className="icon-button"><LayoutGrid className="size-4" /></button>
          <button onClick={onSettings} aria-label="Settings" title="Settings" className="icon-button"><Settings2 className="size-4" /></button>
          <button onClick={onClose} aria-label="Close" title="Close" className="icon-button"><X className="size-4" /></button>
        </div>
      </> : miniLyricsLines === 1 ? <>
        <img src={track.albumArtUrl || '/album-cover.png'} alt="Album cover" className="size-16 shrink-0 rounded-lg object-cover" />
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm">{lyrics?.lines[currentLineIndex]?.text ?? track.title}</p>
          <ScrollingText text={track.artists.join(', ')} className="mt-1 text-xs text-muted-foreground" />
        </div>
        <div className="no-drag"><Controls playing={isPlaying} onControl={onControl} compact /></div>
        <button onClick={() => onModeChange('lyrics')} aria-label="Expand to lyrics" title="Expand to lyrics" className="no-drag icon-button"><Maximize2 className="size-4" /></button>
        <button onClick={onSettings} aria-label="Settings" title="Settings" className="no-drag icon-button"><Settings2 className="size-4" /></button>
        <button onClick={onClose} aria-label="Close" title="Close" className="no-drag icon-button"><X className="size-4" /></button>
      </> : <>
        <div className="flex w-[124px] shrink-0 flex-col items-center justify-center gap-3 overflow-hidden text-center">
          <img src={track.albumArtUrl || '/album-cover.png'} alt="Album cover" className="size-16 shrink-0 self-center rounded-lg object-cover" />
          <ScrollingText text={track.title} align="center" className="w-full px-3 text-xs font-semibold leading-4 text-foreground" />
        </div>
        <div className="-ml-1 my-2 w-px shrink-0 bg-white/[0.14]" />
        <div className="flex min-w-0 flex-1 items-stretch gap-2">
          <div className="flex min-w-0 flex-1 items-center px-2">
            <div className="flex min-w-0 flex-1 flex-col justify-center gap-1">
              {lyrics?.syncLevel !== 'none' && lyrics?.lines.length ? miniLyricSlots(lyrics.lines, currentLineIndex, 3).map((line, index) => {
                const current = index === 1 && currentLineIndex >= 0
                return <p key={index} aria-current={current ? 'true' : undefined} className={`h-8 truncate leading-8 transition-colors duration-200 ${!line ? 'invisible' : current ? 'text-[17px] font-semibold text-foreground' : 'text-[15px] text-muted-foreground/55'}`}>{line?.text ?? ' '}</p>
              }) : lyrics?.lines.length ? <p className="truncate text-sm text-muted-foreground">{lyrics.lines[0].text}</p> : <p className="truncate text-sm text-muted-foreground">{lyricsLoading ? 'Loading lyrics…' : 'Lyrics unavailable'}</p>}
            </div>
          </div>
          <div className="no-drag flex w-[144px] shrink-0 flex-col items-center justify-between py-3">
            <div className="flex items-center gap-0">
              <button onClick={() => onModeChange('lyrics')} aria-label="Expand to lyrics" title="Expand to lyrics" className="icon-button size-7"><Maximize2 className="size-3.5" /></button>
              <button onClick={onSettings} aria-label="Settings" title="Settings" className="icon-button size-7"><Settings2 className="size-3.5" /></button>
              <button onClick={onClose} aria-label="Close" title="Close" className="icon-button size-7"><X className="size-3.5" /></button>
            </div>
            <Controls playing={isPlaying} onControl={onControl} compact />
          </div>
        </div>
      </>}
    </section>
    {clickThrough && <div className="pointer-events-none fixed bottom-1 left-1/2 z-20 -translate-x-1/2 rounded-full border border-amber-200/25 bg-black/80 px-3 py-1 text-[10px] text-amber-100 shadow-lg">Click-through on · Ctrl+Alt+P to interact</div>}
  </main>
}
