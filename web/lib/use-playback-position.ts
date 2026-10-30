import { useEffect, useState } from 'react'
import type { PlaybackSnapshot } from '@/lib/desktop-api'

export function usePlaybackPosition(snapshot: PlaybackSnapshot) {
  const [positionMs, setPositionMs] = useState(0)

  useEffect(() => {
    const update = () => {
      const elapsed = snapshot.isPlaying ? Date.now() - snapshot.observedAtMs : 0
      setPositionMs(Math.min(snapshot.track?.durationMs ?? 0, Math.max(0, snapshot.positionMs + elapsed)))
    }
    update()
    const timer = window.setInterval(update, 100)
    return () => window.clearInterval(timer)
  }, [snapshot])

  return positionMs
}
