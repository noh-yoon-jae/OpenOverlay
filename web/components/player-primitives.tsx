import { useEffect, useRef, useState } from 'react'
import type { CSSProperties, SyntheticEvent } from 'react'
import {
  AlignLeft,
  ChevronLeft,
  ChevronRight,
  LayoutGrid,
  PanelTop,
  Pause,
  Play,
} from 'lucide-react'
import type { AppSettings, OverlayMode, PlaybackAction } from '@/lib/desktop-api'

export function formatTime(milliseconds: number) {
  const seconds = Math.max(0, Math.floor(milliseconds / 1000))
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`
}

export function SeekSlider({ value, durationMs, disabled, className, onPreview, onCommit }: {
  value: number
  durationMs: number
  disabled: boolean
  className: string
  onPreview: (positionMs: number) => void
  onCommit: (positionMs: number) => void
}) {
  const max = Math.max(1, durationMs - 1)
  const position = Math.min(max, Math.max(0, value))
  const commitCurrentValue = (event: SyntheticEvent<HTMLInputElement>) => onCommit(Number(event.currentTarget.value))
  return <input
    type="range"
    aria-label="Seek playback position"
    aria-valuetext={`${formatTime(position)} of ${formatTime(durationMs)}`}
    min="0"
    max={max}
    step="250"
    value={position}
    disabled={disabled}
    onChange={(event) => onPreview(Number(event.target.value))}
    onPointerUp={commitCurrentValue}
    onKeyUp={commitCurrentValue}
    onBlur={commitCurrentValue}
    style={{ '--progress-percent': `${durationMs > 0 ? (position / max) * 100 : 0}%` } as CSSProperties}
    className={`progress-slider ${className}`}
  />
}

export function lyricsFontFamily(font: AppSettings['lyricsFontFamily']) {
  if (font === 'segoe') return '"Segoe UI", sans-serif'
  if (font === 'malgun') return '"Malgun Gothic", sans-serif'
  if (font === 'arial') return 'Arial, sans-serif'
  return 'var(--font-geist-sans), sans-serif'
}

export function ScrollingText({ text, className = '', align = 'left' }: { text: string; className?: string; align?: 'left' | 'center' }) {
  const viewportRef = useRef<HTMLDivElement>(null)
  const textRef = useRef<HTMLSpanElement>(null)
  const [overflow, setOverflow] = useState(false)
  const [duration, setDuration] = useState(8)

  useEffect(() => {
    const viewport = viewportRef.current
    const content = textRef.current
    if (!viewport || !content) return
    const measure = () => {
      const distance = content.scrollWidth
      const isOverflowing = distance > viewport.clientWidth + 1
      setOverflow(isOverflowing)
      setDuration(Math.max(7, distance / 24))
    }
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(viewport)
    observer.observe(content)
    return () => observer.disconnect()
  }, [text])

  return <div ref={viewportRef} className={`mini-marquee-viewport ${className}`} title={text}>
    <div className={`mini-marquee-track ${overflow ? 'is-overflowing' : ''}`} style={{ '--mini-marquee-duration': `${duration}s`, width: overflow ? 'max-content' : '100%', justifyContent: overflow || align === 'left' ? 'flex-start' : 'center' } as CSSProperties}>
      <span ref={textRef} className={`mini-marquee-copy ${overflow || align === 'left' ? '' : 'is-centered'}`}>{text}</span>
      {overflow && <span aria-hidden="true" className="mini-marquee-copy">{text}</span>}
    </div>
  </div>
}

export function SpotifyMark() {
  return <svg viewBox="0 0 24 24" className="size-5 fill-current" aria-label="Spotify"><path d="M17.9 10.9c-3.9-2.3-10.3-2.5-14-.9-.6.2-1.3 0-1.5-.6-.2-.6 0-1.3.6-1.5 4.3-1.9 11.4-1.5 15.9 1.1.5.3.7 1 .4 1.6-.3.5-.9.7-1.4.3Zm-.1 3.9c-.2.4-.8.6-1.2.3-3.3-2-8.3-2.6-12.1-1.4-.5.2-1-.1-1.2-.6-.2-.5.1-1 .6-1.2 4.4-1.3 9.9-.6 13.7 1.7.4.2.5.8.2 1.2Zm-1.4 3.7c-.2.3-.6.4-.9.2-2.9-1.8-6.6-2.2-10.9-1.2-.4.1-.8-.1-.9-.5-.1-.4.1-.8.5-.9 4.7-1.1 8.8-.7 12 1.3.4.2.5.7.2 1.1Z" /></svg>
}

export function Controls({ playing, onControl, compact = false, small = false, medium = false, large = false, tight = false }: { playing: boolean; onControl: (action: PlaybackAction) => void; compact?: boolean; small?: boolean; medium?: boolean; large?: boolean; tight?: boolean }) {
  return <div className={`flex items-center ${large ? 'gap-8' : compact ? tight ? 'gap-0' : 'gap-3' : 'gap-6'}`}>
    <button aria-label="Previous song" onClick={() => onControl('previous')} className="icon-button"><ChevronLeft className="size-4" /></button>
    <button onClick={() => onControl('toggle')} aria-label={playing ? 'Pause' : 'Play'} className={`flex items-center justify-center rounded-full bg-foreground text-background hover:scale-105 ${large ? 'size-11' : medium ? 'size-10' : small ? 'size-9' : compact ? 'size-8' : 'size-10'}`}>
      {playing ? <Pause className={large ? 'size-5' : 'size-4'} fill="currentColor" /> : <Play className={large ? 'size-5' : 'size-4'} fill="currentColor" />}
    </button>
    <button aria-label="Next song" onClick={() => onControl('next')} className="icon-button"><ChevronRight className="size-4" /></button>
  </div>
}

export function ModeSelector({ mode, setMode }: { mode: OverlayMode; setMode: (mode: OverlayMode) => void }) {
  const items = [
    { id: 'overlay' as const, icon: LayoutGrid, label: 'Full overlay' },
    { id: 'lyrics' as const, icon: AlignLeft, label: 'Lyrics only' },
    { id: 'mini' as const, icon: PanelTop, label: 'Mini player' },
  ]
  return <div role="tablist" aria-label="Overlay size" className="flex rounded-lg border border-white/[0.08] bg-white/[0.025] p-1 no-drag">
    {items.map((item) => {
      const Icon = item.icon
      const active = mode === item.id
      return <button key={item.id} role="tab" title={item.label} aria-label={item.label} aria-selected={active} onClick={() => setMode(item.id)} className={`flex items-center justify-center rounded-md p-2 transition-colors ${active ? 'bg-white/[0.1] text-foreground' : 'text-muted-foreground hover:text-foreground'}`}><Icon className="size-3.5" /></button>
    })}
  </div>
}
