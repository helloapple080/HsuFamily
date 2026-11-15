'use client'

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import api from '@/lib/api'
import { maxPhotoSlots, type PhotoKind } from '@/lib/hosted/photo-contract'
import { useAuthStore } from '@/stores/authStore'
import type { ApiResponse } from '@/types/api'

interface PhotoSlot {
  slot: number
  revision?: string
}

export function usePrivatePhotos(kind: PhotoKind, id: string, enabled: boolean) {
  const userId = useAuthStore((state) => state.user?.id)
  const client = useQueryClient()
  const key = ['private-photos', userId, kind, id] as const
  const base = `/api/hosted/photos/${kind}/${id}`
  const slot = z.number().int().min(1).max(maxPhotoSlots(kind))
  const photoSlot = z.union([
    slot.transform((value): PhotoSlot => ({ slot: value })),
    z.object({ slot, revision: z.uuid() }),
  ])
  const list = useQuery({
    queryKey: key,
    enabled: enabled && Boolean(userId),
    queryFn: async ({ signal }) => {
      const { data } = await api.get<ApiResponse<unknown>>(base, { signal })
      return z.array(photoSlot).parse(data.data)
    },
  })
  const refresh = () => client.invalidateQueries({ queryKey: key })
  const upload = useMutation({
    mutationFn: async ({ file, slot }: { file: File; slot: number }) => {
      await api.put(`${base}/${slot}`, file, { headers: { 'Content-Type': file.type } })
    },
    onSuccess: refresh,
  })
  const remove = useMutation({
    mutationFn: async ({ slot, revision }: PhotoSlot) => {
      await api.delete(`${base}/${slot}`, revision ? { headers: { 'If-Match': `"${revision}"` } } : undefined)
    },
    onSuccess: refresh,
  })
  return { base, list, upload, remove }
}
