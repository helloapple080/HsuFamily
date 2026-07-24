import { NextRequest, NextResponse } from 'next/server'

function getApiConnectionSources(apiUrl: string | undefined): string[] {
  if (!apiUrl) {
    return []
  }

  try {
    const parsedApiUrl = new URL(apiUrl)
    if (parsedApiUrl.protocol !== 'http:' && parsedApiUrl.protocol !== 'https:') {
      return []
    }

    const websocketProtocol = parsedApiUrl.protocol === 'https:' ? 'wss:' : 'ws:'
    return [parsedApiUrl.origin, `${websocketProtocol}//${parsedApiUrl.host}`]
  } catch {
    return []
  }
}

export function proxy(request: NextRequest) {
  const nonce = Buffer.from(crypto.randomUUID()).toString('base64')
  const isDevelopment = process.env.NODE_ENV === 'development'
  const connectSources = ["'self'", ...getApiConnectionSources(process.env.NEXT_PUBLIC_API_URL)]
  const contentSecurityPolicy = `
    default-src 'self';
    script-src 'self' 'nonce-${nonce}' 'strict-dynamic'${isDevelopment ? " 'unsafe-eval'" : ''};
    style-src 'self' 'nonce-${nonce}'${isDevelopment ? " 'unsafe-inline'" : ''};
    connect-src ${connectSources.join(' ')};
    img-src 'self' blob: data:;
    font-src 'self';
    object-src 'none';
    base-uri 'self';
    form-action 'self';
    frame-ancestors 'none';
    ${isDevelopment ? '' : 'upgrade-insecure-requests;'}
  `.replace(/\s{2,}/g, ' ').trim()

  const requestHeaders = new Headers(request.headers)
  requestHeaders.set('x-nonce', nonce)
  requestHeaders.set('Content-Security-Policy', contentSecurityPolicy)

  const response = NextResponse.next({
    request: {
      headers: requestHeaders,
    },
  })
  response.headers.set('Content-Security-Policy', contentSecurityPolicy)

  return response
}

export const config = {
  matcher: [
    {
      source: '/((?!api|_next/static|_next/image|favicon.ico).*)',
      missing: [
        { type: 'header', key: 'next-router-prefetch' },
        { type: 'header', key: 'purpose', value: 'prefetch' },
      ],
    },
  ],
}
