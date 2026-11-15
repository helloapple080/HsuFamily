import { fileURLToPath } from 'node:url'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

const local = (path: string) => fileURLToPath(new URL(path, import.meta.url))

export default defineConfig({
  root: local('./spa/'),
  plugins: [react()],
  css: { postcss: local('./') },
  resolve: {
    alias: [
      { find: /^@\/lib\/demo\/(config|fixtures|actions)$/, replacement: local('./spa/private-mode.ts') },
      { find: '@', replacement: local('./src') },
      { find: /^next\/(link|navigation)$/, replacement: local('./spa/navigation.tsx') },
      { find: /^next\/image$/, replacement: local('./spa/image.tsx') },
      { find: /^next-themes$/, replacement: local('./spa/theme.ts') },
    ],
  },
  build: {
    outDir: local('./dist-spa/'),
    emptyOutDir: true,
    assetsInlineLimit: 0,
  },
})
