'use client'

import { useId, useState } from 'react'
import Image from 'next/image'
import { ImageIcon, Trash2 } from 'lucide-react'
import { isAxiosError } from 'axios'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { usePrivatePhotos } from '@/hooks/usePrivatePhotos'
import { maxPhotoSlots, type PhotoKind } from '@/lib/hosted/photo-contract'
import { useAuthStore } from '@/stores/authStore'

interface PrivatePhotosProps {
  kind: PhotoKind
  id: string
  title: string
}

const mimeTypes = ['image/jpeg', 'image/png', 'image/webp']

export default function PrivatePhotos({ kind, id, title }: PrivatePhotosProps) {
  const [open, setOpen] = useState(false)
  const [notice, setNotice] = useState<{ text: string; error?: boolean } | null>(null)
  const sectionId = useId()
  const isAdmin = useAuthStore((state) => state.isAdmin)
  const { base, list, upload, remove } = usePrivatePhotos(kind, id, open)
  const pending = upload.isPending || remove.isPending
  const slots = list.data ?? []
  const capacity = maxPhotoSlots(kind)
  const available = Array.from({ length: capacity }, (_, index) => index + 1).find((slot) => !slots.includes(slot))
  const noun = kind === 'members' ? '頭像' : '照片'

  async function uploadFile(file?: File) {
    if (!file || !available || pending) return
    if (!mimeTypes.includes(file.type) || file.size > 2 * 1024 * 1024) {
      setNotice({ text: '請選擇 2 MiB 以下的 JPEG、PNG 或 WebP。', error: true })
      return
    }
    setNotice(null)
    try {
      await upload.mutateAsync({ file, slot: available })
      setNotice({ text: '照片已上傳' })
    } catch (error) {
      const status = isAxiosError(error) ? error.response?.status : undefined
      if (status === 409) void list.refetch()
      const text = status === 409 ? '這個照片位置已被使用，請重新選擇。'
        : status === 413 || status === 415 ? '照片格式不支援或檔案太大，請縮小或改用 JPEG。' : '無法上傳，請稍後再試。'
      setNotice({ text, error: true })
    }
  }

  async function removePhoto(slot: number) {
    if (pending || !window.confirm(`確定移除「${title}」的${noun}？`)) return
    setNotice(null)
    try {
      await remove.mutateAsync(slot)
      setNotice({ text: '照片已移除' })
    } catch {
      setNotice({ text: '無法移除照片，請稍後再試。', error: true })
    }
  }

  return (
    <div className="space-y-2">
      <Button type="button" variant="ghost" className="h-11"
        aria-expanded={open} aria-controls={sectionId} aria-label={`${open ? '收合' : '查看'}${noun}：${title}`}
        onClick={() => setOpen((value) => !value)}>
        <ImageIcon aria-hidden="true" />{open ? `收合${noun}` : noun}
      </Button>
      <div id={sectionId} hidden={!open} className={open ? 'space-y-3 border-t border-border pt-3' : undefined}>
        {open && list.isPending && <p role="status" className="text-sm text-muted-foreground">正在載入{noun}…</p>}
        {open && list.isError && <div role="alert" className="text-sm">無法載入{noun}。<Button type="button" variant="ghost" onClick={() => void list.refetch()}>重試</Button></div>}
        {open && list.isSuccess && (
          <>
            {slots.length === 0 ? <p className="text-sm text-muted-foreground">這筆紀錄還沒有{noun}。</p> : (
              <ul className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                {slots.map((slot) => <li key={slot} className="space-y-1">
                  {/* 維持同源會話；不能使用 Next 圖片代理轉送私人圖片。 */}
                  <Image unoptimized src={`${base}/${slot}?v=${list.dataUpdatedAt}`} alt={`${title}的${noun} ${slot}`}
                    width={320} height={240} className="max-h-56 w-full rounded-md bg-muted object-contain" />
                  {isAdmin && <Button type="button" variant="ghost" className="h-11" disabled={pending}
                    aria-label={`移除${noun} ${slot}`} onClick={() => void removePhoto(slot)}>
                    <Trash2 aria-hidden="true" />移除
                  </Button>}
                </li>)}
              </ul>
            )}
            {isAdmin && available && (
              <div className="max-w-sm space-y-1">
                <Label htmlFor={`${sectionId}-file`}>選擇照片</Label>
                <Input id={`${sectionId}-file`} type="file" accept={mimeTypes.join(',')} disabled={pending}
                  className="min-h-11" aria-describedby={`${sectionId}-hint`}
                  onChange={(event) => { void uploadFile(event.currentTarget.files?.[0]); event.currentTarget.value = '' }} />
                <p id={`${sectionId}-hint`} className="text-xs text-muted-foreground">選取後上傳。每張 2 MiB 內，僅核准家人可看。</p>
              </div>
            )}
          </>
        )}
        {open && pending && <p role="status" className="text-sm">正在處理照片…</p>}
        {open && notice && <p role={notice.error ? 'alert' : 'status'} className="text-sm">{notice.text}</p>}
      </div>
    </div>
  )
}
