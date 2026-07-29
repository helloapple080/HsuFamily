'use client'

import { useMutation, useQueryClient } from '@tanstack/react-query'
import type { AxiosProgressEvent } from 'axios'
import api from '@/lib/api'
import { isDemoMode } from '@/lib/demo/config'
import { rejectDemoWrite } from '@/lib/demo/actions'
import type { ApiResponse } from '@/types/api'
import type { MediaAssetSummary, UpdateMediaVisibilityInput, UploadMediaInput } from '@/types/media'

function mediaKey(purpose: string, ownerEntityId: string) {
  return ['media', purpose, ownerEntityId] as const
}

function readResponse<T>(response: ApiResponse<T>): T {
  if (!response.success || response.data == null) {
    throw new Error(response.message || '媒體操作失敗')
  }
  return response.data
}

function percentageOf(event: AxiosProgressEvent): number {
  if (!event.total || event.total <= 0) return 0
  return Math.min(100, Math.round((event.loaded / event.total) * 100))
}

export function useUploadMedia() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ file, purpose, ownerEntityId, visibility, onProgress }: UploadMediaInput) => {
      if (isDemoMode) return rejectDemoWrite()
      const body = new FormData()
      body.append('file', file)
      body.append('purpose', purpose)
      body.append('ownerEntityId', ownerEntityId)
      body.append('visibility', visibility)
      const response = await api.post<ApiResponse<MediaAssetSummary>>('/api/media', body, {
        onUploadProgress: (event) => onProgress?.(percentageOf(event)),
      })
      return readResponse(response.data)
    },
    onSuccess: (media) => queryClient.invalidateQueries({ queryKey: mediaKey(media.purpose, media.ownerEntityId) }),
  })
}

export function useUpdateMediaVisibility() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, visibility }: UpdateMediaVisibilityInput) => {
      if (isDemoMode) return rejectDemoWrite()
      const response = await api.patch<ApiResponse<MediaAssetSummary>>(`/api/media/${id}/visibility`, { visibility })
      return readResponse(response.data)
    },
    onSuccess: (media) => queryClient.invalidateQueries({ queryKey: mediaKey(media.purpose, media.ownerEntityId) }),
  })
}

export function useDeleteMedia() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: string) => {
      if (isDemoMode) return rejectDemoWrite()
      const response = await api.delete<ApiResponse<boolean>>(`/api/media/${id}`)
      return readResponse(response.data)
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['media'] }),
  })
}
