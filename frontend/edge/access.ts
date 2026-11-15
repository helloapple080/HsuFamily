import { createRemoteJWKSet, jwtVerify, type JWTVerifyGetKey } from 'jose'
import type { UserInfo } from '@/types/auth'

export interface AccessSettings {
  ACCESS_ISSUER?: string
  ACCESS_AUDIENCE?: string
  PORTAL_ORIGIN?: string
}

interface AccessConfig {
  issuer: string
  audience: string
  origin: string
}

interface AccessIdentity {
  subject: string
  email: string
}

interface AccessRow {
  id: string
  role: UserInfo['role']
}

let keyCache: { issuer: string; load: JWTVerifyGetKey } | undefined

export function accessConfig(env: AccessSettings): AccessConfig | null {
  const { ACCESS_ISSUER: issuer, ACCESS_AUDIENCE: audience, PORTAL_ORIGIN: origin } = env
  if (!issuer || !/^https:\/\/[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.cloudflareaccess\.com$/.test(issuer)) return null
  if (!audience || !/^[a-f0-9]{64}$/.test(audience) || !origin) return null
  try {
    const parsed = new URL(origin)
    return parsed.protocol === 'https:' && parsed.origin === origin ? { issuer, audience, origin } : null
  } catch {
    return null
  }
}

function keysFor(issuer: string) {
  if (keyCache?.issuer !== issuer) {
    keyCache = { issuer, load: createRemoteJWKSet(new URL('/cdn-cgi/access/certs', issuer), { timeoutDuration: 3000 }) }
  }
  return keyCache.load
}

export async function verifyAccess(token: string | null, config: AccessConfig, load: JWTVerifyGetKey = keysFor(config.issuer)): Promise<AccessIdentity | null> {
  if (!token || token.length > 8192 || !/^[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$/.test(token)) return null
  try {
    const { payload } = await jwtVerify(token, load, {
      algorithms: ['RS256'], issuer: config.issuer, audience: config.audience,
      requiredClaims: ['exp', 'iat', 'sub'], clockTolerance: 5,
    })
    const { sub, email, iat } = payload
    if (payload.type !== 'app' || payload.common_name !== undefined || !sub || sub.length > 256) return null
    if (!Number.isSafeInteger(iat) || Number(iat) > Math.floor(Date.now() / 1000) + 5) return null
    if (typeof email !== 'string' || email.length > 254 || !/^[\x21-\x7e]+$/.test(email) || !/^[^@]+@[^@]+\.[^@]+$/.test(email)) return null
    return { subject: sub, email: email.toLowerCase() }
  } catch {
    return null
  }
}

export async function readPortalUser(db: D1Database, identity: AccessIdentity): Promise<UserInfo | null> {
  const { email, subject } = identity
  const find = () => db.prepare('SELECT id, role FROM portal_access WHERE email = ? AND access_subject = ? AND active = 1')
    .bind(email, subject).first<AccessRow>()
  let row = await find()
  if (!row) {
    // 只有已預先核准且尚未綁定的信箱可首次綁定，不能改綁其他 Access 身份。
    row = await db.prepare(`UPDATE portal_access SET access_subject = ?
      WHERE email = ? AND active = 1 AND access_subject IS NULL
      AND NOT EXISTS (SELECT 1 FROM portal_access WHERE access_subject = ?)
      RETURNING id, role`).bind(subject, email, subject).first<AccessRow>() ?? await find()
  }
  if (!row || (row.role !== 'Admin' && row.role !== 'User')) return null
  return { id: row.id, role: row.role, email, username: email, displayName: email.split('@')[0] }
}
