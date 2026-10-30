const { watch } = require('node:fs')
const path = require('node:path')
const { spawn } = require('node:child_process')
const electronPath = require('electron')

const mainPath = path.join(__dirname, 'main.cjs')
const watchedFiles = new Set(['main.cjs', 'preload.cjs'])
let child
let restartRequested = false
let stopping = false
let restartTimer

function startElectron() {
  if (stopping) return
  child = spawn(electronPath, [mainPath], { stdio: 'inherit', env: process.env, windowsHide: false })
  child.on('error', (error) => {
    process.stderr.write(`[OpenOverlay dev] Could not start Electron: ${error.message}\n`)
    process.exitCode = 1
  })
  child.on('exit', (code, signal) => {
    child = undefined
    if (stopping) return
    if (restartRequested) {
      restartRequested = false
      process.stdout.write('[OpenOverlay dev] Restarting Electron after a desktop code change.\n')
      startElectron()
      return
    }
    process.exitCode = code ?? (signal ? 1 : 0)
    process.exit()
  })
}

const watcher = watch(__dirname, (_event, filename) => {
  if (!filename || !watchedFiles.has(filename.toString())) return
  restartRequested = true
  clearTimeout(restartTimer)
  restartTimer = setTimeout(() => {
    if (child && !child.killed) child.kill()
    else if (!child) {
      restartRequested = false
      startElectron()
    }
  }, 180)
})

function stop() {
  if (stopping) return
  stopping = true
  clearTimeout(restartTimer)
  watcher.close()
  if (child && !child.killed) child.kill()
}

process.on('SIGINT', stop)
process.on('SIGTERM', stop)
startElectron()
