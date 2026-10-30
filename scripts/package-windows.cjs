const { spawnSync } = require('node:child_process')
const { copyFile, mkdir, readFile, stat } = require('node:fs/promises')
const os = require('node:os')
const path = require('node:path')

async function main() {
  const projectRoot = path.resolve(__dirname, '..')
  const packageJson = JSON.parse(await readFile(path.join(projectRoot, 'package.json'), 'utf8'))
  const buildRoot = path.join(os.homedir(), '.openoverlay-build')
  const builderOutput = path.join(buildRoot, 'installers')
  const builderCache = path.join(buildRoot, 'cache')
  const installerName = `OpenOverlay-${packageJson.version}-Setup.exe`
  const builderCli = path.join(projectRoot, 'node_modules', 'electron-builder', 'cli.js')

  const result = spawnSync(process.execPath, [
    builderCli,
    '--win',
    'nsis',
    `--config.directories.output=${builderOutput}`,
  ], {
    cwd: projectRoot,
    env: { ...process.env, ELECTRON_BUILDER_CACHE: builderCache },
    stdio: 'inherit',
  })

  if (result.error) throw result.error
  if (result.status !== 0) process.exit(result.status ?? 1)

  const source = path.join(builderOutput, installerName)
  const destinationDirectory = path.join(projectRoot, 'artifacts', 'installers')
  const destination = path.join(destinationDirectory, installerName)
  await stat(source)
  await mkdir(destinationDirectory, { recursive: true })
  await copyFile(source, destination)

  const blockMap = `${source}.blockmap`
  try {
    await stat(blockMap)
    await copyFile(blockMap, `${destination}.blockmap`)
  } catch (error) {
    if (error.code !== 'ENOENT') throw error
  }

  process.stdout.write(`Installer copied to ${destination}\n`)
}

main().catch((error) => {
  process.stderr.write(`Windows packaging failed: ${error.message}\n`)
  process.exitCode = 1
})
