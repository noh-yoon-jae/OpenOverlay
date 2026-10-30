const { app, BrowserWindow, ipcMain, net, protocol, globalShortcut, screen } = require('electron')
const { spawn } = require('node:child_process')
const { createInterface } = require('node:readline')
const { mkdirSync, readFileSync, writeFileSync } = require('node:fs')
const path = require('node:path')
const { pathToFileURL } = require('node:url')

protocol.registerSchemesAsPrivileged([
  { scheme: 'openoverlay', privileges: { standard: true, secure: true, supportFetchAPI: true, stream: true, codeCache: true } },
])

if (!app.requestSingleInstanceLock()) app.quit()

let mainWindow
let settingsWindow
let service
let clickThrough = false
let overlayMode = 'overlay'
let requestId = 0
const pending = new Map()
let latestSnapshot = null
let latestSettings = null
let latestLyricsHealth = null
let startupWindowSizing = false
let startupTargetPosition = null
const overlayBounds = {
  overlay: { width: 960, height: 600 },
  lyrics: { width: 680, height: 460 },
  mini: { width: 640, height: 132 },
}
const startupBounds = { width: 340, height: 360 }
let positionSaveTimer

function boundsForMode(mode) {
  if (mode !== 'mini') return overlayBounds[mode]
  return { width: overlayBounds.mini.width, height: latestSettings?.miniLyricsLines === 3 ? 164 : overlayBounds.mini.height }
}

function resizeMainWindow(mode) {
  if (!mainWindow || mainWindow.isDestroyed()) return
  const bounds = boundsForMode(mode)
  const [x, y] = mainWindow.getPosition()
  // Keep the top-left origin fixed; only the window dimensions change.
  mainWindow.setBounds({ x, y, ...bounds })
}

function overlayPositionPath() {
  return path.join(app.getPath('userData'), 'overlay-position.json')
}

function loadOverlayPosition() {
  try {
    const saved = JSON.parse(readFileSync(overlayPositionPath(), 'utf8'))
    if (Number.isFinite(saved.x) && Number.isFinite(saved.y)) return { x: saved.x, y: saved.y }
  } catch {}
  return null
}

function clampOverlayPosition(position, bounds) {
  const display = screen.getDisplayMatching({ ...position, ...bounds })
  const area = display.workArea
  return {
    x: Math.max(area.x, Math.min(position.x, area.x + area.width - bounds.width)),
    y: Math.max(area.y, Math.min(position.y, area.y + area.height - bounds.height)),
  }
}

function saveOverlayPosition() {
  if (!mainWindow || mainWindow.isDestroyed()) return
  try {
    const [x, y] = mainWindow.getPosition()
    const filePath = overlayPositionPath()
    mkdirSync(path.dirname(filePath), { recursive: true })
    writeFileSync(filePath, JSON.stringify({ x, y }), 'utf8')
  } catch (error) {
    process.stderr.write(`[OpenOverlay] Could not save overlay position: ${error.message}\n`)
  }
}

function scheduleOverlayPositionSave() {
  clearTimeout(positionSaveTimer)
  positionSaveTimer = setTimeout(saveOverlayPosition, 250)
  positionSaveTimer.unref()
}

function broadcast(channel, value) {
  for (const window of [mainWindow, settingsWindow]) {
    if (window && !window.isDestroyed()) window.webContents.send(channel, value)
  }
}

class BackendClient {
  constructor() {
    this.child = this.start()
    this.lines = createInterface({ input: this.child.stdout })
    this.lines.on('line', (line) => this.onLine(line))
    this.child.stderr.on('data', (chunk) => process.stderr.write(`[OpenOverlay .NET] ${chunk}`))
    this.child.on('exit', (code) => {
      const error = new Error(`The OpenOverlay backend exited (${code ?? 'unknown'}).`)
      for (const entry of pending.values()) entry.reject(error)
      pending.clear()
      broadcast('app:error', error.message)
    })
  }

  start() {
    if (app.isPackaged) {
      const executable = path.join(process.resourcesPath, 'backend', 'OpenOverlay.Service.exe')
      return spawn(executable, [], { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] })
    }
    const project = path.join(__dirname, '..', 'src', 'OpenOverlay.Service', 'OpenOverlay.Service.csproj')
    return spawn('dotnet', ['run', '--verbosity', 'quiet', '--project', project, '--no-launch-profile'], {
      cwd: path.dirname(project),
      windowsHide: true,
      stdio: ['pipe', 'pipe', 'pipe'],
      env: process.env,
    })
  }

  onLine(line) {
    let message
    try { message = JSON.parse(line) } catch {
      process.stderr.write(`[OpenOverlay .NET] Ignored invalid protocol line.\n`)
      return
    }
    if (message.event === 'snapshot') {
      latestSnapshot = message.data
      broadcast('app:snapshot', latestSnapshot)
      return
    }
    if (message.event === 'lyrics') {
      broadcast('app:lyrics', message.data)
      return
    }
    if (message.event === 'settings') {
      latestSettings = message.data
      if (mainWindow && !mainWindow.isDestroyed()) {
        mainWindow.setAlwaysOnTop(Boolean(latestSettings.alwaysOnTop), 'screen-saver')
        clickThrough = Boolean(latestSettings.ignoreMouseInput)
        mainWindow.setIgnoreMouseEvents(clickThrough, { forward: true })
        if (overlayMode === 'mini') resizeMainWindow('mini')
        mainWindow.webContents.send('app:click-through', clickThrough)
      }
      broadcast('app:settings', latestSettings)
      return
    }
    if (message.event === 'lyrics.health') {
      latestLyricsHealth = message.data
      broadcast('app:lyrics-health', latestLyricsHealth)
      return
    }
    if (message.event === 'error') {
      broadcast('app:error', message.data)
      return
    }
    if (message.id === undefined) return
    const entry = pending.get(message.id)
    if (!entry) return
    pending.delete(message.id)
    clearTimeout(entry.timeout)
    if (message.error) entry.reject(new Error(message.error.message || 'The backend request failed.'))
    else entry.resolve(message.result)
  }

  call(method, params = {}, timeoutMs = 300000) {
    if (!this.child || this.child.killed || !this.child.stdin.writable) return Promise.reject(new Error('The backend is not running.'))
    const id = ++requestId
    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => {
        pending.delete(id)
        reject(new Error('The backend request timed out.'))
      }, timeoutMs)
      pending.set(id, { resolve, reject, timeout })
      this.child.stdin.write(`${JSON.stringify({ id, method, params })}\n`, (error) => {
        if (!error) return
        clearTimeout(timeout)
        pending.delete(id)
        reject(error)
      })
    })
  }

  stop() {
    if (this.child && !this.child.killed) this.child.kill()
  }
}

function sendError(error) {
  return { ok: false, error: error instanceof Error ? error.message : String(error) }
}

function registerIpc() {
  const register = (channel, method, timeout) => ipcMain.handle(channel, async (_event, ...args) => {
    const params = args.length > 1 ? args : (args.length === 1 ? args[0] : {})
    try { return { ok: true, value: await service.call(method, params, timeout) } }
    catch (error) { return sendError(error) }
  })
  register('app:get-snapshot', 'app.getSnapshot')
  register('app:initialize', 'app.initialize')
  register('settings:get', 'settings.get')
  register('lyrics:health-get', 'lyrics.health.get')
  register('lyrics:providers-check', 'lyrics.providers.checkAll', 180000)
  register('spotify:set-client-id', 'spotify.setClientId')
  register('spotify:connect', 'spotify.connect', 240000)
  register('spotify:disconnect', 'spotify.disconnect')
  register('playback:control', 'playback.control')
  register('playback:seek', 'playback.seek')
  register('playback:set-volume', 'playback.setVolume')
  register('lyrics:refresh', 'lyrics.refresh')
  register('lyrics:set-provider-enabled', 'lyrics.setProviderEnabled')
  register('spotify-lyrics:clear-session', 'spotifyLyrics.clearSession', 300000)
  register('settings:update', 'settings.update')
  ipcMain.handle('spotify-lyrics:enable', async (_event, consented) => {
    const visibleWindows = BrowserWindow.getAllWindows().filter((window) => window.isVisible())
    const restoreState = visibleWindows.map((window) => ({ window, alwaysOnTop: window.isAlwaysOnTop() }))
    for (const { window } of restoreState) {
      window.setAlwaysOnTop(false)
      window.hide()
    }
    try { return { ok: true, value: await service.call('spotifyLyrics.enable', consented, 300000) } }
    catch (error) { return sendError(error) }
    finally {
      for (const { window, alwaysOnTop } of restoreState) {
        if (window.isDestroyed()) continue
        window.setAlwaysOnTop(alwaysOnTop, 'screen-saver')
        window.show()
      }
    }
  })
  ipcMain.handle('window:set-always-on-top', async (_event, enabled) => {
    if (mainWindow && !mainWindow.isDestroyed()) mainWindow.setAlwaysOnTop(Boolean(enabled), 'screen-saver')
    return { ok: true, value: Boolean(enabled) }
  })
  ipcMain.handle('window:set-overlay-opacity', async (event, opacity) => {
    const targetWindow = BrowserWindow.fromWebContents(event.sender)
    if ((targetWindow !== mainWindow && targetWindow !== settingsWindow) || !mainWindow || mainWindow.isDestroyed()) return { ok: false, error: 'Opacity can only be changed from OpenOverlay.' }
    const value = Math.min(1, Math.max(0, Number(opacity)))
    if (!Number.isFinite(value)) return { ok: false, error: 'Opacity value is invalid.' }
    mainWindow.webContents.send('app:overlay-opacity', value)
    return { ok: true, value }
  })
  ipcMain.handle('window:open-settings', async (event) => {
    if (BrowserWindow.fromWebContents(event.sender) !== mainWindow) return { ok: false, error: 'Settings can only be opened from the overlay.' }
    try {
      await openSettingsWindow()
      return { ok: true, value: true }
    } catch (error) { return sendError(error) }
  })
  ipcMain.handle('window:set-overlay-mode', async (event, mode) => {
    if (!Object.hasOwn(overlayBounds, mode)) return { ok: false, error: 'Unknown overlay mode.' }
    const targetWindow = BrowserWindow.fromWebContents(event.sender)
    if (targetWindow !== mainWindow || !mainWindow || mainWindow.isDestroyed()) return { ok: false, error: 'Overlay mode can only be changed from the main window.' }
    overlayMode = mode
    // Keep the top-left corner anchored so mode changes resize down and right.
    resizeMainWindow(mode)
    return { ok: true, value: overlayMode }
  })
  ipcMain.handle('window:get-overlay-mode', (event) => {
    if (BrowserWindow.fromWebContents(event.sender) !== mainWindow) return { ok: false, error: 'Overlay mode can only be read from the main window.' }
    return { ok: true, value: overlayMode }
  })
  ipcMain.handle('window:startup-complete', (event) => {
    if (BrowserWindow.fromWebContents(event.sender) !== mainWindow || !mainWindow || mainWindow.isDestroyed()) return { ok: false, error: 'Startup can only be completed from the main window.' }
    const bounds = boundsForMode(overlayMode)
    const [x, y] = mainWindow.getPosition()
    const [width, height] = mainWindow.getSize()
    const position = startupTargetPosition ?? { x: x + Math.round((width - bounds.width) / 2), y: y + Math.round((height - bounds.height) / 2) }
    const nextPosition = clampOverlayPosition(position, bounds)
    startupWindowSizing = false
    startupTargetPosition = null
    mainWindow.setBounds({ ...nextPosition, ...bounds })
    scheduleOverlayPositionSave()
    return { ok: true, value: true }
  })
  ipcMain.handle('window:close', async (event) => {
    const targetWindow = BrowserWindow.fromWebContents(event.sender)
    if (targetWindow && !targetWindow.isDestroyed()) targetWindow.close()
    return { ok: true, value: true }
  })
  ipcMain.handle('window:minimize', async (event) => {
    const targetWindow = BrowserWindow.fromWebContents(event.sender)
    if (targetWindow && !targetWindow.isDestroyed()) targetWindow.minimize()
    return { ok: true, value: true }
  })
}

async function handleAppProtocol(request) {
  const url = new URL(request.url)
  if (url.hostname !== 'app') return new Response('Not found', { status: 404 })
  let relative
  try { relative = decodeURIComponent(url.pathname.replace(/^\/+/, '')) }
  catch { return new Response('Bad path', { status: 400 }) }
  if (!relative) relative = 'index.html'
  const root = path.resolve(__dirname, '..', 'web', 'out')
  let target = path.resolve(root, relative)
  if (!path.extname(target)) target = path.join(target, 'index.html')
  if (target !== root && !target.toLowerCase().startsWith(`${root.toLowerCase()}${path.sep}`)) {
    return new Response('Forbidden', { status: 403 })
  }
  try { return await net.fetch(pathToFileURL(target).toString()) }
  catch { return new Response('Not found', { status: 404 }) }
}

function configureNavigation(window) {
  window.webContents.setWindowOpenHandler(() => ({ action: 'deny' }))
  window.webContents.on('will-navigate', (event, url) => {
    const devUrl = process.env.OPENOVERLAY_UI_DEV
    let parsed
    try { parsed = new URL(url) } catch { event.preventDefault(); return }
    const isDevUrl = Boolean(devUrl && parsed.protocol === 'http:' && parsed.hostname === '127.0.0.1' && parsed.port === '3000')
    const isAppUrl = parsed.protocol === 'openoverlay:' && parsed.hostname === 'app'
    if (isDevUrl || isAppUrl) return
    event.preventDefault()
  })
}

function windowOptions(options) {
  return {
    frame: false,
    show: false,
    autoHideMenuBar: true,
    icon: path.join(__dirname, 'assets', 'openoverlay-glass-icon.ico'),
    webPreferences: {
      preload: path.join(__dirname, 'preload.cjs'),
      nodeIntegration: false,
      contextIsolation: true,
      sandbox: true,
      webSecurity: true,
    },
    ...options,
  }
}

async function openSettingsWindow() {
  if (!mainWindow || mainWindow.isDestroyed()) return
  if (settingsWindow && !settingsWindow.isDestroyed()) {
    settingsWindow.show()
    settingsWindow.focus()
    return
  }
  settingsWindow = new BrowserWindow(windowOptions({
    parent: mainWindow,
    width: 620,
    height: 760,
    minWidth: 520,
    minHeight: 560,
    resizable: true,
    transparent: true,
    backgroundColor: '#00000000',
    hasShadow: true,
    title: 'OpenOverlay Settings',
  }))
  configureNavigation(settingsWindow)
  settingsWindow.on('closed', () => { settingsWindow = null })
  if (process.env.OPENOVERLAY_UI_DEV) await settingsWindow.loadURL(`${process.env.OPENOVERLAY_UI_DEV}/settings/`)
  else await settingsWindow.loadURL('openoverlay://app/settings/')
  settingsWindow.show()
  if (latestSnapshot) settingsWindow.webContents.send('app:snapshot', latestSnapshot)
  if (latestSettings) settingsWindow.webContents.send('app:settings', latestSettings)
  if (latestLyricsHealth) settingsWindow.webContents.send('app:lyrics-health', latestLyricsHealth)
}

async function createWindow() {
  const initialBounds = startupBounds
  const targetBounds = boundsForMode(overlayMode)
  const savedPosition = loadOverlayPosition()
  startupTargetPosition = savedPosition ? clampOverlayPosition(savedPosition, targetBounds) : null
  const restoredPosition = startupTargetPosition ? clampOverlayPosition({
    x: startupTargetPosition.x + Math.round((targetBounds.width - initialBounds.width) / 2),
    y: startupTargetPosition.y + Math.round((targetBounds.height - initialBounds.height) / 2),
  }, initialBounds) : {}
  startupWindowSizing = true
  mainWindow = new BrowserWindow(windowOptions({
    ...initialBounds,
    ...restoredPosition,
    show: false,
    transparent: true,
    backgroundColor: '#00000000',
    alwaysOnTop: true,
    resizable: false,
    // Native shadows follow the rectangular HWND bounds on Windows and show
    // through the transparent corners; the renderer draws the rounded edge.
    hasShadow: false,
  }))
  configureNavigation(mainWindow)
  mainWindow.on('move', () => { if (!startupWindowSizing) scheduleOverlayPositionSave() })
  mainWindow.webContents.on('did-start-navigation', (_event, _url, inPlace, isMainFrame) => {
    if (!isMainFrame || inPlace || startupWindowSizing || !mainWindow || mainWindow.isDestroyed()) return
    const [x, y] = mainWindow.getPosition()
    const [width, height] = mainWindow.getSize()
    startupTargetPosition = { x: x + Math.round((width - startupBounds.width) / 2), y: y + Math.round((height - startupBounds.height) / 2) }
    startupWindowSizing = true
    mainWindow.setBounds({ ...clampOverlayPosition(startupTargetPosition, startupBounds), ...startupBounds })
  })
  if (process.env.OPENOVERLAY_UI_DEV) await mainWindow.loadURL(process.env.OPENOVERLAY_UI_DEV)
  else await mainWindow.loadURL('openoverlay://app/')
  mainWindow.show()
  mainWindow.on('closed', () => { saveOverlayPosition(); mainWindow = null })
  try {
    const state = await service.call('app.getSnapshot')
    latestSnapshot = state
    broadcast('app:snapshot', state)
    latestSettings = await service.call('settings.get')
    mainWindow.setAlwaysOnTop(Boolean(latestSettings.alwaysOnTop), 'screen-saver')
    clickThrough = Boolean(latestSettings.ignoreMouseInput)
    mainWindow.setIgnoreMouseEvents(clickThrough, { forward: true })
    mainWindow.webContents.send('app:click-through', clickThrough)
    broadcast('app:settings', latestSettings)
  } catch (error) { broadcast('app:error', error.message) }
}

function registerShortcuts() {
  globalShortcut.register('Control+Alt+O', () => {
    if (!mainWindow || mainWindow.isDestroyed()) return
    if (mainWindow.isVisible()) mainWindow.hide()
    else mainWindow.show()
  })
  globalShortcut.register('Control+Alt+P', () => {
    if (!mainWindow || mainWindow.isDestroyed()) return
    clickThrough = !clickThrough
    mainWindow.setIgnoreMouseEvents(clickThrough, { forward: true })
    mainWindow.webContents.send('app:click-through', clickThrough)
    service.call('settings.update', { ignoreMouseInput: clickThrough }).catch(() => {})
  })
  globalShortcut.register('Control+Alt+Left', () => service.call('lyrics.adjustOffset', { deltaMs: -250 }).catch(() => {}))
  globalShortcut.register('Control+Alt+Right', () => service.call('lyrics.adjustOffset', { deltaMs: 250 }).catch(() => {}))
}

app.whenReady().then(async () => {
  protocol.handle('openoverlay', handleAppProtocol)
  service = new BackendClient()
  registerIpc()
  registerShortcuts()
  await createWindow()
  app.on('activate', () => { if (!mainWindow) createWindow() })
})

app.on('second-instance', () => {
  if (mainWindow) { mainWindow.show(); mainWindow.focus() }
})

app.on('before-quit', () => {
  clearTimeout(positionSaveTimer)
  saveOverlayPosition()
  globalShortcut.unregisterAll()
  if (service) service.stop()
})
