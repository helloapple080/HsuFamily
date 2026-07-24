import assert from 'node:assert/strict'
import { existsSync, readFileSync } from 'node:fs'

const proxySource = readFileSync(new URL('../src/proxy.ts', import.meta.url), 'utf8')
const rootLayoutSource = readFileSync(new URL('../src/app/layout.tsx', import.meta.url), 'utf8')
const workspaceSource = readFileSync(new URL('../pnpm-workspace.yaml', import.meta.url), 'utf8')
const sonnerPatchUrl = new URL('../patches/sonner@2.0.7.patch', import.meta.url)
assert.ok(existsSync(sonnerPatchUrl), '必須提供版本化的 Sonner 2.0.7 patch')
const sonnerPatchSource = readFileSync(sonnerPatchUrl, 'utf8')
const styleSourceMatch = proxySource.match(/^\s*style-src .*;$/m)

assert.ok(styleSourceMatch, 'CSP 必須定義 style-src')

const developmentCompatibility = `\${isDevelopment ? " 'unsafe-inline'" : ''}`
const productionStyleSource = styleSourceMatch[0].replace(developmentCompatibility, '')

assert.doesNotMatch(
  productionStyleSource,
  /'unsafe-inline'/,
  "Production style-src 不得包含 'unsafe-inline'",
)
assert.match(
  proxySource,
  /^\s*style-src-attr 'unsafe-inline';$/m,
  'CSP 必須只對 element style attributes 加入相容規則',
)
assert.match(
  rootLayoutSource,
  /^import 'sonner\/dist\/styles\.css'$/m,
  'Root layout 必須明確載入 self-hosted Sonner CSS',
)
assert.match(
  workspaceSource,
  /^patchedDependencies:\n\s+sonner@2\.0\.7: patches\/sonner@2\.0\.7\.patch$/m,
  'pnpm workspace 必須鎖定 Sonner 2.0.7 patch',
)

for (const bundleName of ['index.mjs', 'index.js']) {
  const sectionStart = sonnerPatchSource.indexOf(
    `diff --git a/dist/${bundleName} b/dist/${bundleName}`,
  )
  assert.notEqual(sectionStart, -1, `Sonner patch 必須包含 ${bundleName}`)

  const nextSection = sonnerPatchSource.indexOf('\ndiff --git ', sectionStart + 1)
  const bundlePatch = sonnerPatchSource.slice(
    sectionStart,
    nextSection === -1 ? undefined : nextSection,
  )
  assert.match(
    bundlePatch,
    /function __insertCSS\(code\) \{\n\+\s+return/,
    `Sonner patch 必須停用 ${bundleName} 的 runtime CSS injection`,
  )
}
