import assert from 'node:assert/strict'
import { existsSync } from 'node:fs'
import { readFile, readdir } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const outputRoot = path.join(projectRoot, 'out')
const requiredFiles = [
  'index.html',
  'calendar/index.html',
  'members/index.html',
  'itineraries/index.html',
  '.nojekyll',
]
const forbiddenPatterns = [/mongodb(?:\+srv)?:\/\//i, /Jwt__Secret/i]
assert.ok(!existsSync(path.join(outputRoot, 'api')), '公開展示版不可產生私人 API')
assert.ok(!existsSync(path.join(outputRoot, 'login')), '公開展示版不可提供登入入口')

for (const relativePath of requiredFiles) {
  await readFile(path.join(outputRoot, relativePath))
}

async function collectTextFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true })
  const files = []
  for (const entry of entries) {
    const fullPath = path.join(directory, entry.name)
    if (entry.isDirectory()) files.push(...await collectTextFiles(fullPath))
    else if (/\.(?:html|js|json|txt|css)$/.test(entry.name)) files.push(fullPath)
  }
  return files
}

for (const file of await collectTextFiles(outputRoot)) {
  const content = await readFile(file, 'utf8')
  for (const pattern of forbiddenPatterns) {
    if (pattern.test(content)) throw new Error(`Pages 產物包含禁止內容：${pattern} (${file})`)
  }
}

console.log('GitHub Pages 靜態產物檢查通過。')
