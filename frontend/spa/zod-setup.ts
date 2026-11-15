import { z } from 'zod'

// 先於驗證結構載入：即使探測錯誤會被捕捉，嚴格 CSP 仍會回報 new Function。
z.config({ jitless: true })
