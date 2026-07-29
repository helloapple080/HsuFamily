'use client'

/* eslint-disable @next/next/no-img-element -- 本機 Blob 預覽不應送入 Next.js 圖片最佳化器。 */

import { useEffect, useId, useState } from 'react'
import { z } from 'zod'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { useUploadMedia } from '@/hooks/useMedia'
import type { MediaPurpose, MediaVisibility } from '@/types/media'

const maximumSize = 10 * 1024 * 1024
const acceptedTypes = new Set(['image/jpeg', 'image/png', 'image/webp'])

const uploadSchema = z.object({
  file: z.instanceof(File, { message: '請選擇圖片' })
    .refine((file) => acceptedTypes.has(file.type), '僅支援 JPEG、PNG 或 WebP 圖片')
    .refine((file) => file.size > 0, '圖片不能是空白檔案')
    .refine((file) => file.size <= maximumSize, '圖片大小不得超過 10 MB'),
})

interface MediaUploaderProps {
  purpose: MediaPurpose
  ownerEntityId: string
  onUploaded?: () => void
}

export default function MediaUploader({ purpose, ownerEntityId, onUploaded }: MediaUploaderProps) {
  const inputId = useId()
  const [file, setFile] = useState<File | null>(null)
  const [previewUrl, setPreviewUrl] = useState<string | null>(null)
  const [visibility, setVisibility] = useState<MediaVisibility>('FamilyOnly')
  const [error, setError] = useState<string | null>(null)
  const [progress, setProgress] = useState(0)
  const upload = useUploadMedia()

  useEffect(() => () => {
    if (previewUrl) URL.revokeObjectURL(previewUrl)
  }, [previewUrl])

  function selectFile(nextFile: File | null) {
    setError(null)
    if (previewUrl) URL.revokeObjectURL(previewUrl)
    if (!nextFile) {
      setFile(null)
      setPreviewUrl(null)
      return
    }
    const parsed = uploadSchema.safeParse({ file: nextFile })
    if (!parsed.success) {
      setFile(null)
      setPreviewUrl(null)
      setError(parsed.error.issues[0]?.message ?? '無法使用此圖片')
      return
    }
    setFile(nextFile)
    setPreviewUrl(URL.createObjectURL(nextFile))
  }

  async function submit() {
    const parsed = uploadSchema.safeParse({ file })
    if (!parsed.success) {
      setError(parsed.error.issues[0]?.message ?? '請選擇圖片')
      return
    }
    setError(null)
    setProgress(0)
    try {
      await upload.mutateAsync({ file: parsed.data.file, purpose, ownerEntityId, visibility, onProgress: setProgress })
      selectFile(null)
      onUploaded?.()
    } catch (uploadError: unknown) {
      setError(uploadError instanceof Error && uploadError.message ? uploadError.message : '圖片上傳失敗，請稍後重試')
    }
  }

  return (
    <section aria-label="圖片上傳" className="space-y-3 rounded-lg border border-[var(--border)] bg-[var(--secondary)]/40 p-3">
      <div className="space-y-2">
        <Label htmlFor={inputId}>選擇圖片</Label>
        <Input id={inputId} className="glass-input" type="file" accept="image/jpeg,image/png,image/webp" onChange={(event) => selectFile(event.target.files?.[0] ?? null)} disabled={upload.isPending} />
        <p className="text-xs text-muted-foreground">支援 JPEG、PNG、WebP，單張最大 10 MB。</p>
      </div>
      <fieldset className="space-y-2">
        <legend className="text-sm font-medium">圖片可見性</legend>
        <div className="flex flex-wrap gap-4">
          <Label className="flex cursor-pointer items-center gap-2" htmlFor={`${inputId}-family`}>
            <input id={`${inputId}-family`} type="radio" name={`${inputId}-visibility`} checked={visibility === 'FamilyOnly'} onChange={() => setVisibility('FamilyOnly')} disabled={upload.isPending} />
            僅限家人
          </Label>
          <Label className="flex cursor-pointer items-center gap-2" htmlFor={`${inputId}-public`}>
            <input id={`${inputId}-public`} type="radio" name={`${inputId}-visibility`} checked={visibility === 'Public'} onChange={() => setVisibility('Public')} disabled={upload.isPending} />
            公開
          </Label>
        </div>
        {visibility === 'Public' && <p role="status" className="text-xs text-amber-700 dark:text-amber-300">公開圖片可被未登入者讀取，請確認不含私人資訊。</p>}
      </fieldset>
      {previewUrl && <div className="flex items-center gap-3"><img src={previewUrl} alt="待上傳圖片預覽" className="size-16 rounded-md object-cover" /><Button type="button" variant="ghost" size="sm" onClick={() => selectFile(null)} disabled={upload.isPending}>移除圖片</Button></div>}
      {upload.isPending && <p role="status" className="text-sm text-muted-foreground">上傳中 {progress}%</p>}
      {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
      <Button type="button" className="glass-button" onClick={submit} disabled={upload.isPending}>{upload.isPending ? '上傳中…' : '上傳圖片'}</Button>
    </section>
  )
}
