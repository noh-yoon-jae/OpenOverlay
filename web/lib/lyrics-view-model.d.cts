import type { LyricLine, LyricsSnapshot } from './desktop-api'

export function currentLyricIndex(lines: LyricLine[], syncLevel: LyricsSnapshot['syncLevel'], positionMs: number): number
export function miniLyricSlots(lines: LyricLine[], currentIndex: number, count: 1 | 3): Array<LyricLine | undefined>
export function shouldAutoFollow(manualScroll: boolean, modeChangePending: boolean): boolean
export function scheduleScrollResume(schedule: (callback: () => void, delay: number) => number, cancel: (timer: number) => void, previousTimer: number | null, onResume: () => void): number
export function afterLayout(requestFrame: (callback: () => void) => void, onAligned: () => void): void
