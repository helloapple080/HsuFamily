import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

const proxySource = readFileSync(new URL('../src/proxy.ts', import.meta.url), 'utf8')
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
