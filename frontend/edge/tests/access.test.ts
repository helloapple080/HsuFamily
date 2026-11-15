// @vitest-environment node
import { beforeAll, describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { DatabaseSync } from 'node:sqlite'
import { createLocalJWKSet, exportJWK, generateKeyPair, SignJWT, type JWTVerifyGetKey } from 'jose'
import { accessConfig, readPortalUser, verifyAccess } from '../access'

const issuer = 'https://family.cloudflareaccess.com'
const audience = 'a'.repeat(64)
const origin = 'https://family.example.com'
const config = { issuer, audience, origin }
const env = { ACCESS_ISSUER: issuer, ACCESS_AUDIENCE: audience, PORTAL_ORIGIN: origin }
let key: CryptoKey
let wrongKey: CryptoKey
let getKey: JWTVerifyGetKey

beforeAll(async () => {
  const keys = await generateKeyPair('RS256')
  key = keys.privateKey
  wrongKey = (await generateKeyPair('RS256')).privateKey
  getKey = createLocalJWKSet({ keys: [{ ...await exportJWK(keys.publicKey), kid: 'test' }] })
})

function token({ iss = issuer, aud = audience, sub = 'family-identity', email = 'Reader@example.com', exp = '5m', privateKey = key, type = 'app' } = {}) {
  return new SignJWT({ email, type }).setProtectedHeader({ alg: 'RS256', kid: 'test' })
    .setIssuer(iss).setAudience(aud).setSubject(sub).setIssuedAt().setExpirationTime(exp).sign(privateKey)
}

describe('Cloudflare Access 驗證', () => {
  it('只接受明確的 HTTPS 網址、Access issuer 與 application audience', () => {
    expect(accessConfig(env)).toEqual(config)
    expect(accessConfig({})).toBeNull()
    expect(accessConfig({ ...env, ACCESS_ISSUER: 'https://family.cloudflareaccess.com.attacker.example' })).toBeNull()
    expect(accessConfig({ ...env, PORTAL_ORIGIN: `${origin}/path` })).toBeNull()
  })

  it('接受正確簽章並正規化真人電子郵件', async () => {
    expect(await verifyAccess(await token(), config, getKey)).toEqual({ subject: 'family-identity', email: 'reader@example.com' })
  })

  it('拒絕缺少、過期、錯誤對象、錯誤簽章、服務權杖與組織權杖', async () => {
    expect(await verifyAccess(null, config, getKey)).toBeNull()
    for (const input of [
      { exp: '-5m' }, { aud: 'b'.repeat(64) }, { iss: 'https://other.cloudflareaccess.com' },
      { privateKey: wrongKey }, { sub: '' }, { email: '' }, { type: 'org' },
    ]) {
      expect(await verifyAccess(await token(input), config, getKey)).toBeNull()
    }
  })

  it('首次綁定核准信箱，禁止身分互換、服務自行加入、重新啟用停權帳號', async () => {
    const sqlite = new DatabaseSync(':memory:')
    try {
      sqlite.exec(readFileSync(new URL('../migrations/0001_portal.sql', import.meta.url), 'utf-8'))
      const db = { prepare: (sql: string) => ({ bind: (...bindings: string[]) => ({ first: async () => sqlite.prepare(sql).get(...bindings) ?? null }) }) } as unknown as D1Database
      const seed = sqlite.prepare('INSERT INTO portal_access(id,email,role) VALUES (?,?,?)')
      const email = 'reader@example.com'
      const identity = { subject: 'approved-subject', email }
      const otherEmail = 'other@example.com'
      seed.run(crypto.randomUUID(), email, 'User')
      seed.run(crypto.randomUUID(), otherEmail, 'Admin')
      expect(await readPortalUser(db, { ...identity, email: 'unknown@example.com' })).toBeNull()
      expect(await readPortalUser(db, identity)).toMatchObject({ role: 'User', email })
      expect(await readPortalUser(db, identity)).toMatchObject({ role: 'User', email })
      expect(await readPortalUser(db, { ...identity, email: otherEmail })).toBeNull()
      expect(await readPortalUser(db, { ...identity, subject: 'other-subject' })).toBeNull()
      sqlite.prepare('UPDATE portal_access SET active = 0 WHERE email = ?').run(email)
      expect(await readPortalUser(db, identity)).toBeNull()
    } finally { sqlite.close() }
  })
})
