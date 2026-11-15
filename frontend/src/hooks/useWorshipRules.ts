'use client'

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import api from '@/lib/api'
import { worshipRule, type WorshipInput } from '@/lib/hosted/worship-contract'
import { useAuthStore } from '@/stores/authStore'
import type { ApiResponse } from '@/types/api'
import { versionHeaders, type RecordVersion } from '@/lib/recordVersion'

interface SaveRule {
  id?: string
  input: WorshipInput
  version?: number
}

const url = '/api/hosted/worship'

export function useWorshipRules() {
  const userId = useAuthStore((state) => state.user?.id)
  const queryKey = ['hosted-worship', userId] as const
  const client = useQueryClient()
  const refresh = () => client.invalidateQueries({ queryKey })

  const rules = useQuery({
    queryKey,
    enabled: Boolean(userId),
    staleTime: 0,
    refetchOnWindowFocus: true,
    queryFn: async ({ signal }) => {
      const { data } = await api.get<ApiResponse<unknown>>(url, { signal })
      return z.array(worshipRule).parse(data.data)
    },
  })
  const save = useMutation({
    mutationFn: async ({ id, input, version }: SaveRule) => {
      if (id) await api.put(`${url}/${id}`, input, versionHeaders(version))
      else await api.post(url, input)
    },
    onSuccess: refresh,
  })
  const remove = useMutation({
    mutationFn: async ({ id, version }: RecordVersion) => { await api.delete(`${url}/${id}`, versionHeaders(version)) },
    onSuccess: refresh,
  })
  return { rules, save, remove }
}
