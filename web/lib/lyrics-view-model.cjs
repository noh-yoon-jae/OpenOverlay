function currentLyricIndex(lines, syncLevel, positionMs) {
  if (!lines.length || syncLevel === 'none') return -1
  let index = -1
  for (let i = 0; i < lines.length; i += 1) {
    if (lines[i].startMs <= positionMs) index = i
    else break
  }
  return index
}

function miniLyricSlots(lines, currentIndex, count) {
  const offsets = count === 3 ? [-1, 0, 1] : [0]
  const anchor = Math.max(0, currentIndex)
  return offsets.map((offset) => {
    const index = anchor + offset
    return index >= 0 ? lines[index] : undefined
  })
}

function shouldAutoFollow(manualScroll, modeChangePending) {
  return !manualScroll && !modeChangePending
}

function scheduleScrollResume(schedule, cancel, previousTimer, onResume) {
  if (previousTimer !== null) cancel(previousTimer)
  return schedule(() => onResume(), 3000)
}

function afterLayout(requestFrame, onAligned) {
  requestFrame(() => requestFrame(onAligned))
}

module.exports = { currentLyricIndex, miniLyricSlots, shouldAutoFollow, scheduleScrollResume, afterLayout }
