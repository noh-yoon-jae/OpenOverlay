import type { KeyboardEvent, RefObject } from 'react'
import type { AppSettings, LyricsSnapshot } from '@/lib/desktop-api'
import { formatTime, lyricsFontFamily } from '@/components/player-primitives'

type LyricsViewportProps = {
  lyrics: LyricsSnapshot
  currentLineIndex: number
  rowHeight: number
  viewportHeight: number
  fontSize: number
  fontFamily: AppSettings['lyricsFontFamily']
  highlightCurrentLine: boolean
  canSeek: boolean
  viewportRef: RefObject<HTMLDivElement | null>
  onWheel: () => void
  onSeek: (positionMs: number) => void
}

export function LyricsViewport({
  lyrics,
  currentLineIndex,
  rowHeight,
  viewportHeight,
  fontSize,
  fontFamily,
  highlightCurrentLine,
  canSeek,
  viewportRef,
  onWheel,
  onSeek,
}: LyricsViewportProps) {
  return <div ref={viewportRef} onWheel={onWheel} style={{ height: `${viewportHeight}px` }} className="lyric-window">
    <div aria-live="polite" className="flex flex-col">
      {lyrics.syncLevel !== 'none' && <div aria-hidden="true" style={{ height: `${rowHeight * 2}px`, flex: '0 0 auto' }} />}
      {lyrics.lines.map((line, index) => {
        const seekable = lyrics.syncLevel !== 'none' && canSeek
        const current = index === currentLineIndex
        const seekToLine = () => {
          if (seekable) onSeek(line.startMs)
        }
        const handleKeyDown = (event: KeyboardEvent<HTMLParagraphElement>) => {
          if (seekable && (event.key === 'Enter' || event.key === ' ')) {
            event.preventDefault()
            seekToLine()
          }
        }
        return <p
          key={`${line.startMs}-${index}`}
          data-lyric-index={index}
          role={seekable ? 'button' : undefined}
          tabIndex={seekable ? 0 : undefined}
          aria-label={seekable ? `Seek to ${formatTime(line.startMs)}: ${line.text}` : undefined}
          onClick={seekToLine}
          onKeyDown={handleKeyDown}
          title={seekable ? `Seek to ${formatTime(line.startMs)}` : undefined}
          style={{
            height: `${rowHeight}px`,
            flex: '0 0 auto',
            fontFamily: lyricsFontFamily(fontFamily),
            fontSize: `${lyrics.syncLevel !== 'none' && current && highlightCurrentLine ? fontSize : Math.max(14, Math.round(fontSize * 0.68))}px`,
          }}
          className={`flex items-center truncate transition-colors duration-300 ${seekable ? 'cursor-pointer hover:text-foreground focus-visible:outline focus-visible:outline-1 focus-visible:outline-spotify' : ''} ${lyrics.syncLevel === 'none' ? 'text-muted-foreground/75' : current && highlightCurrentLine ? 'font-semibold leading-tight tracking-tight text-foreground' : index < currentLineIndex ? 'text-muted-foreground/35' : 'text-muted-foreground/70'}`}
        >{line.text}</p>
      })}
      {lyrics.syncLevel !== 'none' && <div aria-hidden="true" style={{ height: `${rowHeight * 2}px`, flex: '0 0 auto' }} />}
    </div>
  </div>
}
