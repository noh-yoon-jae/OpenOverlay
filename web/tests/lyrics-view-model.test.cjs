const test = require('node:test')
const assert = require('node:assert/strict')
const { currentLyricIndex, miniLyricSlots, shouldAutoFollow, scheduleScrollResume, afterLayout } = require('../lib/lyrics-view-model.cjs')

const lines = [
  { text: 'one', startMs: 1000 },
  { text: 'two', startMs: 2000 },
  { text: 'three', startMs: 3000 },
  { text: 'four', startMs: 4000 },
]

test('current lyric follows the active timestamp and leaves pre-roll empty', () => {
  assert.equal(currentLyricIndex(lines, 'line', 0), -1)
  assert.equal(currentLyricIndex(lines, 'line', 2000), 1)
  assert.equal(currentLyricIndex(lines, 'line', 5000), 3)
})

test('plain lyrics do not claim a synchronized active line', () => {
  assert.equal(currentLyricIndex(lines, 'none', 2500), -1)
})

test('three-line mini view pads the start and end without changing its row count', () => {
  assert.deepEqual(miniLyricSlots(lines, 0, 3), [undefined, lines[0], lines[1]])
  assert.deepEqual(miniLyricSlots(lines, 2, 3), [lines[1], lines[2], lines[3]])
  assert.deepEqual(miniLyricSlots(lines, 3, 3), [lines[2], lines[3], undefined])
})

test('one-line mini view contains only the current lyric', () => {
  assert.deepEqual(miniLyricSlots(lines, 2, 1), [lines[2]])
})

test('auto-follow pauses during manual scroll and mode realignment', () => {
  assert.equal(shouldAutoFollow(false, false), true)
  assert.equal(shouldAutoFollow(true, false), false)
  assert.equal(shouldAutoFollow(false, true), false)
})

test('manual wheel input resets the three-second resume timer', () => {
  const scheduled = []
  const cancelled = []
  const schedule = (callback, delay) => { scheduled.push({ callback, delay }); return scheduled.length }
  const cancel = (timer) => cancelled.push(timer)
  let resumed = 0
  scheduleScrollResume(schedule, cancel, null, () => { resumed += 1 })
  const nextTimer = scheduleScrollResume(schedule, cancel, 1, () => { resumed += 1 })
  assert.deepEqual(cancelled, [1])
  assert.equal(scheduled[1].delay, 3000)
  assert.equal(nextTimer, 2)
  scheduled[1].callback()
  assert.equal(resumed, 1)
})

test('mode change realignment waits for two layout frames', () => {
  const frames = []
  let aligned = false
  afterLayout((callback) => frames.push(callback), () => { aligned = true })
  assert.equal(frames.length, 1)
  frames.shift()()
  assert.equal(aligned, false)
  assert.equal(frames.length, 1)
  frames.shift()()
  assert.equal(aligned, true)
})
