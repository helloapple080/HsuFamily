'use client'

import { useRef, useState } from 'react'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'

interface ConfirmDialogProps {
  trigger: React.ReactElement
  title: string
  description: string
  confirmLabel?: string
  onConfirm: () => Promise<void> | void
}

/// 提供焦點管理、Escape 取消與非同步等待狀態的危險操作確認視窗。
export default function ConfirmDialog({
  trigger,
  title,
  description,
  confirmLabel = '確認刪除',
  onConfirm,
}: ConfirmDialogProps) {
  const [open, setOpen] = useState(false)
  const [isPending, setIsPending] = useState(false)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const cancelRef = useRef<HTMLButtonElement>(null)

  const handleOpenChange = (nextOpen: boolean) => {
    if (isPending) return
    setOpen(nextOpen)
    if (!nextOpen) setErrorMessage(null)
  }

  const handleConfirm = async () => {
    setIsPending(true)
    setErrorMessage(null)
    try {
      await onConfirm()
      setOpen(false)
    } catch {
      setErrorMessage('操作失敗，請稍後再試。')
    } finally {
      setIsPending(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogTrigger render={trigger} />
      <DialogContent initialFocus={cancelRef} showCloseButton={!isPending}>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{description}</DialogDescription>
        </DialogHeader>
        {errorMessage && <p role="alert" className="text-sm text-destructive">{errorMessage}</p>}
        <DialogFooter>
          <Button ref={cancelRef} type="button" variant="outline" disabled={isPending} onClick={() => setOpen(false)}>
            取消
          </Button>
          <Button type="button" variant="destructive" disabled={isPending} onClick={handleConfirm}>
            {isPending ? '刪除中…' : confirmLabel}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
