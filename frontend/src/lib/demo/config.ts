export const isDemoMode = process.env.NEXT_PUBLIC_DEMO_MODE === 'true'
export const demoBasePath = isDemoMode ? '/HsuFamily' : ''
export const demoReadOnlyMessage = '公開展示模式不會儲存任何變更。'
