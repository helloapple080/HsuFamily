import { rename, writeFile } from 'node:fs/promises'
import { spawn } from 'node:child_process'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const temporaryMoves = [
  ['src/proxy.ts', 'src/proxy.ts.demo-disabled'],
  ['src/app/(auth)/login/page.tsx', 'src/app/(auth)/login/page.tsx.demo-disabled'],
]

async function move([source, destination]) {
  await rename(path.join(projectRoot, source), path.join(projectRoot, destination))
}

async function runBuild() {
  const nextBinary = path.join(projectRoot, 'node_modules', '.bin', 'next')
  await new Promise((resolve, reject) => {
    const child = spawn(nextBinary, ['build'], {
      cwd: projectRoot,
      env: { ...process.env, NEXT_PUBLIC_DEMO_MODE: 'true' },
      stdio: 'inherit',
    })
    child.once('error', reject)
    child.once('exit', (code) => code === 0 ? resolve() : reject(new Error(`Next.js build 失敗，exit code ${code}`)))
  })
}

const moved = []
try {
  for (const entry of temporaryMoves) {
    await move(entry)
    moved.push(entry)
  }
  await runBuild()
  await writeFile(path.join(projectRoot, 'out', '.nojekyll'), '', 'utf8')
} finally {
  for (const [source, destination] of moved.reverse()) {
    await rename(path.join(projectRoot, destination), path.join(projectRoot, source))
  }
}
