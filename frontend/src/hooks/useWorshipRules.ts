'use client'

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import api from '@/lib/api'
import { worshipRule, type WorshipInput } from '@/lib/hosted/worship-contract'
import { useAuthStore } from '@/stores/authStore'
import type { ApiResponse } from '@/types/api'

interface SaveRule {
  id?: string
  input: WorshipInput
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
    mutationFn: async ({ id, input }: SaveRule) => {
      if (id) await api.put(`${url}/${id}`, input)
      else await api.post(url, input)
    },
    onSuccess: refresh,
  })
  const remove = useMutation({
    mutationFn: async (id: string) => { await api.delete(`${url}/${id}`) },
    onSuccess: refresh,
  })
  return { rules, save, remove }
}
